#!/usr/bin/env python3
"""Zoom Clipboard for Linux: private local browser UI and optional desktop tray."""
import base64
import hashlib
import http.server
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser

CLIENT_ID = "Sj25UHYTRWOYWhAbDqIGmA"
PORT = 8765
ORIGIN = f"http://127.0.0.1:{PORT}"
REDIRECT = ORIGIN + "/callback"
CONFIG_DIR = Path(os.environ.get("XDG_CONFIG_HOME", str(Path.home() / ".config"))) / "zoom-clipboard"
MAX_FILE = 20 * 1024 * 1024


class ZoomError(Exception):
    def __init__(self, message, status=0):
        super().__init__(message)
        self.status = status


class ZoomRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        target = urllib.parse.urlparse(newurl)
        if target.scheme != "https" or not (target.hostname == "zoom.us" or (target.hostname or "").endswith(".zoom.us")):
            raise ZoomError("Zoom returned a redirect outside its trusted HTTPS domains.")
        # The Zoom file-upload API requires preserving POST and authentication on redirects.
        return urllib.request.Request(newurl, data=request.data, headers=dict(request.headers), method=request.get_method())


class App:
    def __init__(self):
        CONFIG_DIR.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.config_path = CONFIG_DIR / "config.json"
        self.config = json.loads(self.config_path.read_text()) if self.config_path.exists() else {}
        self.auth = None
        self.secret_tool = shutil.which("secret-tool")
        self.lock = threading.RLock()
        self.oauth = None
        self.session = secrets.token_urlsafe(32)
        self.deleted = set()
        self.server = None
        self.tray_available = False
        if self.secret_tool:
            result = subprocess.run([self.secret_tool, "lookup", "application", "zoom-clipboard"], capture_output=True, text=True, timeout=15)
            if result.returncode == 0 and result.stdout.strip():
                self.auth = json.loads(result.stdout)

    def save_config(self):
        tmp = self.config_path.with_suffix(".tmp")
        tmp.write_text(json.dumps(self.config, indent=2))
        tmp.chmod(0o600)
        tmp.replace(self.config_path)

    def save_auth(self, auth):
        self.auth = auth
        if self.secret_tool:
            result = subprocess.run([self.secret_tool, "store", "--label=Zoom Clipboard OAuth", "application", "zoom-clipboard"],
                                    input=json.dumps(auth), capture_output=True, text=True, timeout=30)
            if result.returncode != 0:
                raise ZoomError("Signed in for this session, but the desktop keyring could not save the login. Unlock your keyring and sign in again.")

    @staticmethod
    def request(url, method="GET", data=None, headers=None):
        req = urllib.request.Request(url, data=data, headers=headers or {}, method=method)
        try:
            with urllib.request.build_opener(ZoomRedirect()).open(req, timeout=120) as response:
                body = response.read()
                return json.loads(body) if body else {}
        except urllib.error.HTTPError as error:
            body = error.read(1000).decode(errors="replace")
            raise ZoomError(f"Zoom returned {error.code}: {body}", error.code) from error

    def token(self):
        override = os.environ.get("ZOOM_CLIPBOARD_TOKEN")
        if override:
            return override
        with self.lock:
            if not self.auth:
                raise ZoomError("Sign in with Zoom first.")
            if self.auth["expires_at"] < time.time() + 120:
                body = urllib.parse.urlencode({"grant_type": "refresh_token", "refresh_token": self.auth["refresh_token"],
                                               "client_id": self.auth["client_id"]}).encode()
                updated = self.request("https://zoom.us/oauth/token", "POST", body, {"Content-Type": "application/x-www-form-urlencoded"})
                updated["client_id"] = self.auth["client_id"]
                updated["expires_at"] = time.time() + updated.get("expires_in", 3600)
                updated.setdefault("refresh_token", self.auth["refresh_token"])
                self.save_auth(updated)
            return self.auth["access_token"]

    def api(self, path, method="GET", payload=None):
        data = json.dumps(payload).encode() if payload is not None else None
        return self.request("https://api.zoom.us/v2" + path, method, data,
                            {"Authorization": "Bearer " + self.token(), "Content-Type": "application/json"})

    def status(self):
        signed_in = bool(self.auth or os.environ.get("ZOOM_CLIPBOARD_TOKEN"))
        return {"signed_in": signed_in, "channel": self.config.get("channel"),
                "client_id": self.config.get("client_id", CLIENT_ID), "persistent_login": bool(self.secret_tool),
                "tray": self.tray_available, "complete": signed_in and bool(self.config.get("channel"))}

    def begin_login(self, client_id):
        with self.lock:
            if self.oauth and self.oauth["expires"] > time.time():
                return {"url": self.oauth["url"]}
            client_id = client_id.strip()
            if not client_id:
                raise ZoomError("Public Client ID is required.")
            verifier = secrets.token_urlsafe(32)
            challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip("=")
            state = secrets.token_urlsafe(32)
            url = "https://zoom.us/oauth/authorize?" + urllib.parse.urlencode({"response_type": "code", "client_id": client_id,
                  "redirect_uri": REDIRECT, "code_challenge": challenge, "code_challenge_method": "S256", "state": state})
            self.oauth = {"verifier": verifier, "state": state, "client_id": client_id, "expires": time.time() + 300, "url": url}
            return {"url": url}

    def callback(self, query):
        with self.lock:
            flow = self.oauth
            if not flow or flow["expires"] < time.time() or not secrets.compare_digest(query.get("state", [""])[0], flow["state"]):
                raise ZoomError("Sign-in expired or the callback did not match. Start sign-in again.")
            self.oauth = None
            if query.get("error") or not query.get("code"):
                raise ZoomError("Zoom authorization was cancelled. You can sign in again.")
            data = urllib.parse.urlencode({"grant_type": "authorization_code", "client_id": flow["client_id"],
                "code": query["code"][0], "redirect_uri": REDIRECT, "code_verifier": flow["verifier"]}).encode()
            auth = self.request("https://zoom.us/oauth/token", "POST", data, {"Content-Type": "application/x-www-form-urlencoded"})
            auth["client_id"] = flow["client_id"]
            auth["expires_at"] = time.time() + auth.get("expires_in", 3600)
            self.save_auth(auth)
            self.config["client_id"] = flow["client_id"]
            self.save_config()

    def channels(self):
        channels = []
        page = ""
        while True:
            result = self.api("/chat/users/me/channels?" + urllib.parse.urlencode({"page_size": 100, "next_page_token": page}))
            channels.extend(c for c in result.get("channels", []) if not c["id"].startswith("web_ins_"))
            page = result.get("next_page_token")
            if not page:
                return sorted(channels, key=lambda c: c.get("name", "").lower())

    def select_channel(self, channel_id):
        channel = next((c for c in self.channels() if c["id"] == channel_id), None)
        if not channel:
            raise ZoomError("Choose a channel you belong to.")
        self.config["channel"] = {"id": channel["id"], "name": channel.get("name", channel["id"])}
        self.save_config()
        return self.status()

    def create_channel(self, name):
        name = name.strip()
        if not 1 <= len(name) <= 80:
            raise ZoomError("Enter a channel name between 1 and 80 characters.")
        channel = self.api("/chat/users/me/channels", "POST", {"name": name, "type": 1,
            "channel_settings": {"add_member_permissions": 2, "allow_to_add_external_users": 0}})
        if not channel.get("id"):
            raise ZoomError("Zoom did not return the new channel ID.")
        self.config["channel"] = {"id": channel["id"], "name": channel.get("name", name)}
        self.save_config()
        return self.status()

    def selector(self):
        channel = self.config.get("channel")
        if not channel:
            raise ZoomError("Choose a destination channel first.")
        return urllib.parse.urlencode({"to_channel": channel["id"]})

    def files(self):
        files = {}
        page = ""
        for _ in range(5):
            result = self.api("/chat/users/me/messages?" + self.selector() + "&" + urllib.parse.urlencode({"page_size": 100, "next_page_token": page}))
            for message in result.get("messages", []):
                for file in message.get("files", []):
                    file_id = file.get("file_id")
                    name = file.get("file_name")
                    if not file_id or not name or file_id in self.deleted:
                        continue
                    files[file_id] = {"id": file_id, "name": name, "size": file.get("file_size", 0),
                                      "timestamp": message.get("timestamp", message.get("message_timestamp", 0))}
            page = result.get("next_page_token")
            if not page:
                break
        return sorted(files.values(), key=lambda f: f["timestamp"], reverse=True)

    def link(self, file_id):
        link = self.api("/chat/files/" + urllib.parse.quote(file_id, safe=""))["download_url"]
        if urllib.parse.urlparse(link).scheme != "https":
            raise ZoomError("Zoom returned an invalid download URL.")
        return {"url": link}

    def upload(self, filename, content):
        if len(content) > MAX_FILE:
            raise ZoomError("Files must be no larger than 20 MB.")
        safe_name = Path(filename).name.replace('"', "_").replace("\r", "_").replace("\n", "_")
        boundary = "ZoomClipboard" + secrets.token_hex(16)
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="{safe_name}"\r\n'
                'Content-Type: application/octet-stream\r\n\r\n').encode() + content
        body += (f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="to_channel"\r\n\r\n'
                 f'{self.config["channel"]["id"]}\r\n--{boundary}--\r\n').encode()
        result = self.request("https://file.zoom.us/v2/chat/users/me/messages/files", "POST", body,
                              {"Authorization": "Bearer " + self.token(), "Content-Type": "multipart/form-data; boundary=" + boundary})
        message_id = result.get("id")
        if not message_id:
            raise ZoomError("Upload accepted, but Zoom returned no message ID. Refresh the file list before uploading again.")
        for attempt in range(12):
            if attempt:
                time.sleep(1)
            try:
                message = self.api("/chat/users/me/messages/" + urllib.parse.quote(message_id, safe="") + "?" + self.selector())
            except ZoomError as error:
                if error.status == 404 or (error.status == 400 and "5401" in str(error)):
                    continue
                raise
            file_id = next((f.get("file_id") for f in message.get("files", []) if f.get("file_id")), None)
            file_id = file_id or next(iter(message.get("file_ids", [])), None) or message.get("file_id")
            if file_id:
                return self.link(file_id)
        return {"uploaded": True, "warning": "File uploaded. Zoom has not indexed its link yet; refresh shortly."}


class Handler(http.server.BaseHTTPRequestHandler):
    app = None
    def log_message(self, format, *args):
        pass  # OAuth codes, state and download links must not appear in logs.

    def send(self, data, code=200, content_type="application/json"):
        body = json.dumps(data).encode() if content_type == "application/json" else data
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        route = urllib.parse.urlparse(self.path)
        if self.headers.get("Host") != f"127.0.0.1:{PORT}":
            self.send({"error": "Invalid host"}, 403)
            return
        if route.path == "/":
            self.send(Path(__file__).with_name("dashboard.html").read_bytes(), content_type="text/html; charset=utf-8")
        elif route.path == "/callback":
            try:
                self.app.callback(urllib.parse.parse_qs(route.query))
                self.send(b'<!doctype html><title>Zoom Clipboard</title><p>Signed in. Return to the Zoom Clipboard tab to continue.</p>', content_type="text/html; charset=utf-8")
            except Exception:
                self.send(b'<!doctype html><title>Zoom Clipboard</title><p>Sign-in could not complete. Return to Zoom Clipboard and try again.</p>', 400, "text/html; charset=utf-8")
        else:
            self.send({"error": "Not found"}, 404)

    def do_POST(self):
        if (self.headers.get("Host") != f"127.0.0.1:{PORT}" or self.headers.get("Origin") != ORIGIN or
                not secrets.compare_digest(self.headers.get("X-Session", ""), self.app.session)):
            self.send({"error": "Open the dashboard from the application launcher."}, 403)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 <= size <= MAX_FILE:
                self.send({"error": "Files must be no larger than 20 MB."}, 413)
                return
            raw = self.rfile.read(size)
            if self.path == "/api/upload":
                self.app.selector()
                result = self.app.upload(urllib.parse.unquote(self.headers.get("X-Filename", "file")), raw)
            else:
                body = json.loads(raw or b"{}")
                result = self.dispatch(body)
            self.send(result)
        except Exception as error:
            self.send({"error": str(error)}, 400)

    def dispatch(self, body):
        app = self.app
        if self.path == "/api/status": return app.status()
        if self.path == "/api/login": return app.begin_login(body.get("client_id", CLIENT_ID))
        if self.path == "/api/channels": return app.channels()
        if self.path == "/api/select": return app.select_channel(body["id"])
        if self.path == "/api/create": return app.create_channel(body["name"])
        if self.path == "/api/files": return app.files()
        if self.path == "/api/link": return app.link(body["id"])
        if self.path == "/api/delete":
            app.api("/chat/files/" + urllib.parse.quote(body["id"], safe=""), "DELETE")
            app.deleted.add(body["id"])
            return {"deleted": True}
        if self.path == "/api/logout":
            if app.secret_tool:
                subprocess.run([app.secret_tool, "clear", "application", "zoom-clipboard"], check=True, timeout=15)
            app.auth = None
            app.oauth = None
            return app.status()
        if self.path == "/api/exit":
            threading.Thread(target=app.server.shutdown, daemon=True).start()
            return {"exiting": True}
        raise ZoomError("Unknown action.")


def run_tray(app, url):
    try:
        import gi
        gi.require_version("Gtk", "3.0")
        gi.require_version("AyatanaAppIndicator3", "0.1")
        from gi.repository import Gtk, AyatanaAppIndicator3, GLib
        if not Gtk.init_check()[0]:
            return False
    except (ImportError, ValueError):
        return False
    indicator = AyatanaAppIndicator3.Indicator.new("zoom-clipboard", "folder-remote", AyatanaAppIndicator3.IndicatorCategory.APPLICATION_STATUS)
    indicator.set_status(AyatanaAppIndicator3.IndicatorStatus.ACTIVE)
    menu = Gtk.Menu()
    open_item = Gtk.MenuItem(label="Open Zoom Clipboard")
    open_item.connect("activate", lambda _: webbrowser.open(url))
    menu.append(open_item)
    exit_item = Gtk.MenuItem(label="Exit")
    exit_item.connect("activate", lambda _: threading.Thread(target=app.server.shutdown, daemon=True).start())
    menu.append(exit_item)
    menu.show_all()
    indicator.set_menu(menu)
    app.tray_available = True
    GLib.timeout_add(500, lambda: True if app.server_thread.is_alive() else (Gtk.main_quit() or False))
    Gtk.main()
    return True


def main():
    import fcntl
    CONFIG_DIR.mkdir(parents=True, exist_ok=True, mode=0o700)
    lock_dir = Path(os.environ.get("XDG_RUNTIME_DIR", str(CONFIG_DIR)))
    lock_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
    lock = open(lock_dir / "zoom-clipboard.lock", "a+")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        lock.seek(0)
        url = lock.read().strip()
        if url.startswith(ORIGIN + "/#"):
            webbrowser.open(url)
        return
    os.chmod(lock.name, 0o600)
    app = App()
    Handler.app = app
    try:
        app.server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    except OSError:
        raise SystemExit("Port 8765 is occupied. Close the other Zoom sign-in or local service and try again.")
    url = ORIGIN + "/#" + app.session
    lock.seek(0)
    lock.truncate()
    lock.write(url)
    lock.flush()
    app.server_thread = threading.Thread(target=app.server.serve_forever, daemon=True)
    app.server_thread.start()
    if "--no-browser" not in __import__("sys").argv:
        webbrowser.open(url)
    print("Zoom Clipboard is running locally. Re-run the launcher to reopen; use Exit in the dashboard to stop.")
    try:
        if not run_tray(app, url):
            app.server_thread.join()
    except KeyboardInterrupt:
        app.server.shutdown()
    finally:
        app.server.server_close()
        lock.close()


if __name__ == "__main__":
    main()
