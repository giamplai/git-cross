#!/usr/bin/env bash
# Build release packages into artifacts/packages.
#   scripts/package.sh                 -> every target
#   scripts/package.sh linux-x64 win-x64
# Linux: .tar.gz + .deb · Windows: .zip · macOS: unsigned .app in a .zip. SHA256SUMS is written at the end.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1

VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
NAME=git-cross
OUT="$PWD/artifacts/packages"
TARGETS=("$@"); [ ${#TARGETS[@]} -eq 0 ] && TARGETS=(linux-x64 linux-arm64 osx-arm64 osx-x64 win-x64)
rm -rf "$OUT"; mkdir -p "$OUT"

for rid in "${TARGETS[@]}"; do
  echo "== $rid"
  pub="artifacts/publish/$rid"
  rm -rf "$pub"
  dotnet publish src/GitCross.App -c Release -r "$rid" --self-contained -p:PublishSingleFile=true -o "$pub" --nologo -v q
  find "$pub" -name '*.pdb' -delete
  base="$NAME-$VERSION-$rid"
  case "$rid" in
    linux-*)
      stage=$(mktemp -d)/"$base"; mkdir -p "$stage"
      cp "$pub/$NAME" "$stage/"; cp src/GitCross.App/Assets/icon.png "$stage/git-cross.png"
      cp packaging/linux/git-cross.desktop "$stage/"
      cp LICENSE.md NOTICE.md THIRD-PARTY-NOTICES.md "$stage/"
      cat > "$stage/install.sh" <<'EOS'
#!/usr/bin/env sh
# Per-user install: ~/.local/opt + launcher entry. Remove with: rm -r ~/.local/opt/git-cross ~/.local/bin/git-cross ~/.local/share/applications/git-cross.desktop
set -e
here=$(cd "$(dirname "$0")" && pwd)
dest="$HOME/.local/opt/git-cross"
mkdir -p "$dest" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
cp "$here/git-cross" "$dest/"; chmod +x "$dest/git-cross"
ln -sf "$dest/git-cross" "$HOME/.local/bin/git-cross"
cp "$here/git-cross.png" "$HOME/.local/share/icons/hicolor/256x256/apps/"
sed "s|^Exec=.*|Exec=$dest/git-cross %F|" "$here/git-cross.desktop" > "$HOME/.local/share/applications/git-cross.desktop"
echo "Installed. Start it from your application menu or run: git-cross"
EOS
      chmod +x "$stage/install.sh" "$stage/$NAME"
      tar -C "$(dirname "$stage")" -czf "$OUT/$base.tar.gz" "$base"

      arch=amd64; [ "$rid" = linux-arm64 ] && arch=arm64
      deb=$(mktemp -d)
      mkdir -p "$deb/DEBIAN" "$deb/opt/git-cross" "$deb/usr/bin" "$deb/usr/share/applications" "$deb/usr/share/icons/hicolor/256x256/apps"
      cp "$pub/$NAME" "$deb/opt/git-cross/"; chmod 755 "$deb/opt/git-cross/$NAME"
      ln -s /opt/git-cross/$NAME "$deb/usr/bin/git-cross"
      cp src/GitCross.App/Assets/icon.png "$deb/usr/share/icons/hicolor/256x256/apps/git-cross.png"
      cp packaging/linux/git-cross.desktop "$deb/usr/share/applications/"
      doc="$deb/usr/share/doc/git-cross"; mkdir -p "$doc"
      cp NOTICE.md THIRD-PARTY-NOTICES.md "$doc/"
      cat > "$doc/copyright" <<'EOC'
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: git-cross
Source: https://github.com/gitextensions/gitextensions (inspiration only; no upstream code is included - see NOTICE.md)

Files: *
Copyright: git-cross contributors
License: GPL-3
 On Debian systems the complete text of the GNU General Public License
 version 3 can be found in /usr/share/common-licenses/GPL-3.

Files: opt/git-cross/git-cross
Copyright: see THIRD-PARTY-NOTICES.md in this directory
Comment: the single-file executable bundles the .NET runtime, Avalonia, SkiaSharp, HarfBuzzSharp and the Inter font
 (MIT, BSD-3-Clause, SIL OFL 1.1). Their notices and license texts are reproduced in THIRD-PARTY-NOTICES.md.
License: MIT and BSD-3-Clause and OFL-1.1
EOC
      # Debian policy: root dir and all dirs 0755, plain files 0644, the executable 0755
      find "$deb" -type d -exec chmod 755 {} +
      find "$deb" -type f ! -path "*/opt/*" -exec chmod 644 {} +
      size=$(du -sk "$deb/opt" | cut -f1)
      cat > "$deb/DEBIAN/control" <<EOC
Package: git-cross
Version: $VERSION
Section: devel
Priority: optional
Architecture: $arch
Depends: git, libfontconfig1, libx11-6
Installed-Size: $size
Maintainer: git-cross contributors <noreply@example.com>
Description: Cross-platform Git client
 A fast, native Git client with history graph, staging by hunk and line,
 interactive rebase, blame, stashes, submodules and conflict resolution.
EOC
      dpkg-deb --build --root-owner-group "$deb" "$OUT/git-cross_${VERSION}_${arch}.deb" >/dev/null
      ;;
    win-*)
      stage=$(mktemp -d)/"$base"; mkdir -p "$stage"
      cp "$pub/$NAME.exe" "$stage/"
      printf 'git-cross %s\r\nRequires git (https://git-scm.com). Run %s.exe.\r\nLicense: GNU GPL v3 (LICENSE.md). Inspired by Git Extensions - not affiliated. See NOTICE.md and THIRD-PARTY-NOTICES.md.\r\n' "$VERSION" "$NAME" > "$stage/README.txt"
      cp LICENSE.md NOTICE.md THIRD-PARTY-NOTICES.md "$stage/"
      (cd "$(dirname "$stage")" && zip -qr "$OUT/$base.zip" "$base")
      ;;
    osx-*)
      stage=$(mktemp -d); app="$stage/git-cross.app"
      mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
      cp "$pub/$NAME" "$app/Contents/MacOS/"; chmod +x "$app/Contents/MacOS/$NAME"
      cp src/GitCross.App/Assets/icon.icns "$app/Contents/Resources/"
      cp LICENSE.md NOTICE.md THIRD-PARTY-NOTICES.md "$app/Contents/Resources/"
      sed "s/@VERSION@/$VERSION/g" packaging/macos-Info.plist > "$app/Contents/Info.plist"
      (cd "$stage" && zip -qry "$OUT/$base.zip" "git-cross.app")
      ;;
  esac
done

(cd "$OUT" && sha256sum * > SHA256SUMS)
echo; echo "Packages in $OUT:"; ls -lh "$OUT"
