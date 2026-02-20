# AmbiMatter SimHub Plugin (Phase 2)

The C# SimHub plugin is planned for Phase 2. It will:

- Read iRacing telemetry (time of day, weather) from SimHub
- Calculate target Kelvin and brightness via a three-layer atmospheric model
  (Solar + Weather + Local/Tunnel)
- Apply intensity scoring to avoid unnecessary updates
- Send zone-aware UDP commands to the Python Matter bridge
- Expose a settings UI for zone configuration and parameter tuning

See `CLAUDE.md` for the full design specification.

## Planned File Structure

```
plugin/
├── SmartAmbientMatter.sln
└── SmartAmbientMatter/
    ├── SmartAmbientMatter.csproj
    ├── AmbiMatterPlugin.cs       # Main plugin class (IPlugin, IDataPlugin)
    ├── AtmosphereEngine.cs       # Three-layer calculation engine
    ├── ZoneManager.cs            # Zone config + per-zone state tracking
    ├── TransitionCalculator.cs   # Dynamic transition + Guillotine logic
    ├── UdpSender.cs              # Zone-aware UDP sender
    ├── Settings/
    │   ├── SettingsControl.xaml
    │   └── SettingsControl.xaml.cs
    └── Models/
        ├── Zone.cs
        └── LightingState.cs
```
