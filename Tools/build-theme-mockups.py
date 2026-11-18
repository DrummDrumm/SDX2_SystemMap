"""Turn the HUD theme mockups in Themes/source (Design canvas .dc.html files) into stand-alone pages and PNGs.

Each theme file passes its colours to a shared template: Main (Phosphor), Tachi, Unn, Belter and Protogen use HudMap;
Contact, InnerRed, Ember, NavyChart and BelterHex (Belter) use ChartMap (radar contact plot crossed with the inner-system chart).
This script inlines the template, both components' renderVals() code and a tiny renderer for the template's
{{holes}} and <sc-for> loops, so every page opens in a browser with no Design runtime. With --png it also saves a
1600x900 screenshot of each page through headless Edge (or Chrome).

    python -I Tools/build-theme-mockups.py [--png] [HudMap|ChartMap|<theme file> ...]
"""
import base64
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Themes", "source")
OUT = os.path.join(ROOT, "Themes")
THEMES = [("HudMap", "Main", "phosphor"), ("HudMap", "Tachi", "tachi-red"), ("HudMap", "Unn", "naval-ice"),
          ("HudMap", "Belter", "belter-amber"), ("HudMap", "Protogen", "protogen-cyan"),
          ("ChartMap", "Contact", "chart-contact-green"), ("ChartMap", "InnerRed", "chart-inner-red"),
          ("ChartMap", "Ember", "chart-ember"), ("ChartMap", "NavyChart", "chart-navy"), ("ChartMap", "BelterHex", "chart-belter")]
HEX_BLOB = "/_blob/15e8ef30a2e99a1d067151de86e0968e"  # hex-tile.svg as uploaded to the Design canvas

RUNTIME = r"""
class DCLogic { constructor(props) { this.props = props || {}; } }
function get(scope, path) {
  if (/^\d+(\.\d+)?$/.test(path)) return Number(path);
  return path.split('.').reduce(function (o, k) { return o == null ? undefined : o[k]; }, scope);
}
function esc(v) { return String(v == null ? '' : v).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
function render(tpl, scope) {
  tpl = tpl.replace(/<sc-for list="\{\{\s*([\w.$]+)\s*\}\}" as="(\w+)"[^>]*>([\s\S]*?)<\/sc-for>/g, function (m, path, as, body) {
    return (get(scope, path) || []).map(function (it, i) { var s = Object.assign({}, scope); s[as] = it; s.$index = i; return render(body, s); }).join('');
  });
  return tpl.replace(/\{\{\s*([\w.$]+)\s*\}\}/g, function (m, p) { return esc(get(scope, p)); });
}
"""


def part(text, pattern, what, name):
    m = re.search(pattern, text, re.S)
    if not m:
        sys.exit(f"{name}: no {what} found")
    return m.group(1)


def component(path):
    text = open(path, encoding="utf-8").read()
    svg = base64.b64encode(open(os.path.join(SRC, "hex-tile.svg"), "rb").read()).decode()
    text = text.replace(HEX_BLOB, "data:image/svg+xml;base64," + svg)
    helmet = re.search(r"<helmet>(.*?)</helmet>", text, re.S)
    name = os.path.basename(path)
    markup = part(text, r"<x-dc>(.*?)</x-dc>", "<x-dc> markup", name)
    markup = re.sub(r"<helmet>.*?</helmet>", "", markup, flags=re.S)
    props = part(text, r"data-props='(.*?)'", "data-props", name)
    defaults = {k: v["default"] for k, v in json.loads(props.replace("&#39;", "'").replace("&amp;", "&")).items()
                if isinstance(v, dict) and "default" in v}
    code = part(text, r"<script type=\"text/x-dc\"[^>]*>(.*?)</script>", "component script", name)
    title = part(text, r"<title>(.*?)</title>", "title", name)
    return markup, defaults, code, title, helmet.group(1) if helmet else ""


def page(template, theme_file):
    hud_markup, _, hud_code, _, head = component(os.path.join(SRC, template + ".dc.html"))
    _, defaults, theme_code, title, _ = component(os.path.join(SRC, theme_file + ".dc.html"))
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>{title}</title>
{head}
<style>html,body{{margin:0;background:#000}}</style>
</head>
<body>
<div id="root"></div>
<script>
{RUNTIME}
var Theme = (function () {{ {theme_code}
return Component; }})();
var Hud = (function () {{ {hud_code}
return Component; }})();
var theme = new Theme({json.dumps(defaults)}).renderVals().theme;
var tpl = {json.dumps(hud_markup)};
document.getElementById('root').innerHTML = render(tpl, new Hud({{ theme: theme }}).renderVals());
</script>
</body>
</html>
"""


def browser():
    for p in [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
              r"C:\Program Files\Google\Chrome\Application\chrome.exe", shutil.which("msedge") or "", shutil.which("chrome") or ""]:
        if p and os.path.exists(p):
            return p
    return None


def main():
    png = "--png" in sys.argv
    exe = browser() if png else None
    if png and not exe:
        print("no Edge or Chrome found: pages only")
    os.makedirs(os.path.join(OUT, "png"), exist_ok=True)
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    for template, src, slug in THEMES:
        if only and src not in only and template not in only:
            continue
        html = os.path.join(OUT, slug + ".html")
        with open(html, "w", encoding="utf-8", newline="\n") as f:
            f.write(page(template, src))
        print("wrote", os.path.relpath(html, ROOT))
        if exe:
            shot = os.path.join(OUT, "png", slug + ".png")
            subprocess.run([exe, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                            "--window-size=1600,900", "--virtual-time-budget=4000", "--screenshot=" + shot,
                            "file:///" + html.replace("\\", "/")], check=False, capture_output=True, timeout=120)
            print("  png" if os.path.exists(shot) else "  png FAILED", os.path.relpath(shot, ROOT))


if __name__ == "__main__":
    main()
