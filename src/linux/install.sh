#!/bin/sh
set -eu
SOURCE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
INSTALL_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/zoom-clipboard"
APPLICATIONS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$INSTALL_DIR" "$APPLICATIONS_DIR"
cp "$SOURCE_DIR/zoom_clipboard.py" "$SOURCE_DIR/dashboard.html" "$SOURCE_DIR/zoom-clipboard" "$INSTALL_DIR/"
chmod 755 "$INSTALL_DIR/zoom-clipboard" "$INSTALL_DIR/zoom_clipboard.py"
python3 - "$INSTALL_DIR" "$APPLICATIONS_DIR" <<'PY'
import pathlib, sys
app, applications = map(pathlib.Path, sys.argv[1:])
launcher = str(app / 'zoom-clipboard').replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$').replace('%', '%%')
(applications / 'zoom-clipboard.desktop').write_text('[Desktop Entry]\nType=Application\nName=Zoom Clipboard\nComment=Transfer files through Zoom Team Chat\nExec="'+launcher+'"\nIcon=folder-remote\nTerminal=false\nCategories=Network;Utility;\n')
PY
printf 'Installed. Open Zoom Clipboard from your application menu.\n'
