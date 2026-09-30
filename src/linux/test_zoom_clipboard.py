"""Offline regression checks. Never access a Zoom account or delete real files."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from unittest.mock import patch


class RegressionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.env = patch.dict(os.environ, {"XDG_CONFIG_HOME": self.temp.name})
        self.env.start()
        self.addCleanup(self.env.stop)
        spec = importlib.util.spec_from_file_location("zoom_clipboard", Path(__file__).with_name("zoom_clipboard.py"))
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        with patch.object(self.module.shutil, "which", return_value=None):
            self.app = self.module.App()
        self.app.config["channel"] = {"id": "private-channel", "name": "Clipboard"}

    def test_deleted_references_hidden_and_empty_files_preserved(self):
        self.app.api = lambda *args: {"messages": [{"timestamp": 123, "files": [
            {"file_id": "deleted", "file_size": 0},
            {"file_id": "empty", "file_name": "empty.txt", "file_size": 0},
            {"file_id": "live", "file_name": "hello.txt", "file_size": 50}]}]}
        self.app.deleted.add("live")
        self.assertEqual([f["id"] for f in self.app.files()], ["empty"])

    def test_config_survives_restart_without_tokens(self):
        self.app.save_config()
        saved = json.loads(self.app.config_path.read_text())
        self.assertEqual(saved["channel"]["id"], "private-channel")
        self.assertNotIn("access_token", saved)

    def test_create_is_private(self):
        calls = []
        self.app.api = lambda *args: calls.append(args) or {"id": "new", "name": "Private"}
        self.app.create_channel("Private")
        self.assertEqual(calls[0][2]["type"], 1)
        self.assertEqual(self.app.config["channel"]["id"], "new")

    def test_oauth_state_rejected_without_token_exchange(self):
        self.app.begin_login("public-id")
        with self.assertRaises(self.module.ZoomError):
            self.app.callback({"state": ["wrong"], "code": ["code"]})

    def test_redirect_rejects_external_host(self):
        handler = self.module.ZoomRedirect()
        request = urllib.request.Request("https://file.zoom.us/upload", data=b"file", headers={"Authorization": "Bearer test"})
        with self.assertRaises(self.module.ZoomError):
            handler.redirect_request(request, None, 307, "", {}, "https://example.org/upload")
        redirected = handler.redirect_request(request, None, 307, "", {}, "https://file.zoom.us/next")
        self.assertEqual(redirected.get_method(), "POST")
        self.assertEqual(redirected.data, b"file")

    def test_http_requires_session_and_origin(self):
        self.module.Handler.app = self.app
        server = self.module.http.server.ThreadingHTTPServer(("127.0.0.1", 0), self.module.Handler)
        self.app.server = server
        self.module.PORT = server.server_port
        self.module.ORIGIN = f"http://127.0.0.1:{server.server_port}"
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            request = urllib.request.Request(self.module.ORIGIN + "/api/status", data=b"{}", method="POST")
            with self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(request)
            self.assertEqual(error.exception.code, 403)
            request.add_header("Origin", self.module.ORIGIN)
            request.add_header("X-Session", self.app.session)
            with urllib.request.urlopen(request) as response:
                self.assertEqual(response.status, 200)
                self.assertFalse(json.loads(response.read())["signed_in"])
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == "__main__":
    unittest.main()
