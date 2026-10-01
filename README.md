# Wind Turbine Digital Twin

A live 3 MW wind turbine in the browser: a physics-based simulation, a SCADA-style dashboard and a fault scenario
with graded protection, derating and lost production. Runs on phones and desktops.

**[Open the live demo](https://sultud.github.io/windfarm-digital-twin/)**

[![Wind turbine digital twin with a dark SCADA-style dashboard](Assets/WebGLTemplates/WindFarm/TemplateData/preview.png)](https://sultud.github.io/windfarm-digital-twin/)

## Try this

1. Raise the wind above rated (~11 m/s) and watch the pitch controller shed power at 3 MW.
2. Press **FAN FAILURE** (phone: **FAULT**). The generator heats up, the controller raises a warning, then derates,
   then trips. **SHOW** takes the camera to the drivetrain with the housing in X-Ray.
3. **REPAIR** and watch the turbine cool down, restart and ramp back to rated, while the dashboard counts the lost
   production.

## What it shows

- **Operator view:** state at a glance (producing, rated, derated, storm shutdown, fault stop) with a plain-language
  explanation, power with an "≈ homes" equivalent, wind, rotor speed, generator temperature, energy.
- **Engineer view:** power curve with the live operating point and its recent trail, trends with real limits, blade
  pitch on the 3D rotor, alarms graded the way an IEC 61400-25 controller reports them.
- **Owner view:** production lost to the fault, in kWh.

## The simulation

The telemetry comes from a mock SCADA source built after the industry reference models (the NREL 5-MW control
structure behind OpenFAST and the IEC 61400-27-1 simplified models), parameterized as a generic V112-class turbine:

- Wind: mean + slow drift + Ornstein-Uhlenbeck turbulence + IEC-style gusts.
- Rotor: one-mass torque balance `J·dω/dt = T_aero - T_gen` with a `Cp(λ, β)` surface, so the rotor stores and
  releases kinetic energy and measured points scatter on both sides of the power curve, as in field data.
- Control: generator torque regions 1.5 / 2 / 3, collective pitch PI with anti-windup above rated, a supervisory
  state machine with hysteresis and confirmation timers, storm shutdown and restart.
- Thermal and protection: lumped generator heat balance with an explicit cooling path; warning, alarm with derating,
  latched over-temperature trip, restart once cooled and repaired.
- Sensors: noise only on the published readings, never on the physics state.

The steady state matches the computed datasheet power curve to 0.001 MW; turbulence, gust, lull and start-up cases
were verified with a console harness.

## A swappable data source

The dashboard and the 3D model bind only to `ITurbineTelemetrySource`. Alarms, derating and trips reach them through
the telemetry, exactly as from a real turbine, so a live SCADA, MQTT or WebSocket feed can replace the simulator
without touching the visualization. Only the fault button is a mock "what-if" tool, like an operator training
simulator.

## Built with

- Unity 6.3 LTS, URP, WebGL; UI Toolkit with `Painter2D` charts and a raw-mesh scatter layer
- C#: plain-C# models, MonoBehaviours only for the simulator and its consumers
- 3D model made in Blender (~2.3k triangles)
- Custom shaders: gradient sky, ground status ring, obstruction lights, horizon silhouettes
- Developed with AI assistance (Claude Code)

Source layout: `Assets/_Project/Scripts` (`Simulation`, `UI`, `Visuals`, `Cameras`), UI assets in
`Assets/_Project/UI`, WebGL page template in `Assets/WebGLTemplates/WindFarm`.

## Credits and license

Made by **Süleyman Nizamoğlu** ·
[LinkedIn](https://www.linkedin.com/in/s%C3%BCleyman-nizamo%C4%9Flu-98531123b/)

© 2026 Süleyman Nizamoğlu. All rights reserved. The source is shared for review only; no license is granted to
copy, modify or redistribute it.

Fonts: Barlow, Barlow Condensed and IBM Plex Mono, under the SIL Open Font License 1.1.
