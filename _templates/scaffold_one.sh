#!/usr/bin/env bash
set -e
NAME="$1"
ROOT=/d/SkiaSharp/demos
T=/d/SkiaSharp/_templates
APPDIR="$ROOT/$NAME"
PROJDIR="$APPDIR/$NAME"
LOWER=$(echo "$NAME" | tr '[:upper:]' '[:lower:]')

if [ ! -f "$APPDIR/$NAME.sln" ]; then
  dotnet new unoapp -o "$APPDIR" -n "$NAME" -preset blank -tfm net10.0 -platforms desktop -theme fluent -markup xaml -skip --no-update-check >/dev/null 2>&1
fi

cp "$T/global.json" "$APPDIR/global.json"
sed "s/__NSLOWER__/$LOWER/g; s/__NS__/$NAME/g" "$T/App.csproj.tmpl"   > "$PROJDIR/$NAME.csproj"
sed "s/__NS__/$NAME/g" "$T/DemoScene.cs.tmpl"      > "$PROJDIR/DemoScene.cs"
sed "s/__NS__/$NAME/g" "$T/DemoCanvas.cs.tmpl"     > "$PROJDIR/DemoCanvas.cs"
sed "s/__NS__/$NAME/g" "$T/MainPage.xaml.tmpl"     > "$PROJDIR/MainPage.xaml"
sed "s/__NS__/$NAME/g" "$T/MainPage.xaml.cs.tmpl"  > "$PROJDIR/MainPage.xaml.cs"
sed "s/__NS__/$NAME/g" "$T/Thumb.cs.tmpl"          > "$PROJDIR/Thumb.cs"
sed "s/__NS__/$NAME/g" "$T/Program.cs.tmpl"        > "$PROJDIR/Platforms/Desktop/Program.cs"
echo "scaffolded $NAME -> $PROJDIR"
