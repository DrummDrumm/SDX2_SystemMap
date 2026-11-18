# HUD theme mockups

Five looks for the system map, all in one HUD style: a bearing ruler, a framed map with a lock reticle on the
destination, data columns on both sides and a status bar. They all show the same example route, Earth to Ceres by flip and
burn.

| Theme | Page | Picture | In the plugin |
|---|---|---|---|
| Phosphor (green and gold) | [phosphor.html](phosphor.html) | [png](png/phosphor.png) | not yet |
| Tachi Red (MCRN) | [tachi-red.html](tachi-red.html) | [png](png/tachi-red.png) | not yet (the plugin's Tachi is the older look) |
| Naval Ice (UNN) | [naval-ice.html](naval-ice.html) | [png](png/naval-ice.png) | **yes**: combined with Navy Chart as theme `UNN` |
| Belter Amber (OPA) | [belter-amber.html](belter-amber.html) | [png](png/belter-amber.png) | not yet |
| Protogen Cyan | [protogen-cyan.html](protogen-cyan.html) | [png](png/protogen-cyan.png) | **yes**: theme `Protogen` |
| Navy Chart (UNN, chart layout) | [chart-navy.html](chart-navy.html) | [png](png/chart-navy.png) | **yes**: combined with Naval Ice as theme `UNN` |

- `source/`: the Design canvas files. `HudMap.dc.html` is the shared layout. The other files each hold one theme's
  colours (`Main.dc.html` is Phosphor).
- The `.html` pages open in any browser. Rebuild them, and the PNGs, after editing `source/`:

```bash
python -I Tools/build-theme-mockups.py --png
```
