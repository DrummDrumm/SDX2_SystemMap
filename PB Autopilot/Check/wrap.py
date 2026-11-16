# Wraps ..\Protogen Systems - Autopilot.cs the way the game compiles a PB script: inside a MyGridProgram subclass, with its usings.
import os
here = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(here, "..", "Protogen Systems - Autopilot.cs"), encoding="utf-8").read().lstrip("﻿")
head = ("using System;using System.Collections;using System.Collections.Generic;using System.Collections.Immutable;using System.Linq;"
        "using System.Text;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using SpaceEngineers.Game.ModAPI.Ingame;"
        "using VRage;using VRage.Collections;using VRage.Game;using VRage.Game.Components;using VRage.Game.GUI.TextPanel;"
        "using VRage.Game.ModAPI.Ingame;using VRage.Game.ModAPI.Ingame.Utilities;using VRage.Game.ObjectBuilders.Definitions;using VRageMath;\n"
        "namespace PbCheck{public partial class Program:MyGridProgram{\n")
os.makedirs(os.path.join(here, "obj"), exist_ok=True)
open(os.path.join(here, "obj", "Wrapped.cs"), "w", encoding="utf-8").write(head + src + "\n}}\n")
