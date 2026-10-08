# HWiNFODeck

HWiNFODeck is an unsigned Macro Deck 3 development plugin that exposes HWiNFO sensor
readings through the HWiNFO Shared Memory Interface. HWiNFO is installed separately;
the plugin does not bundle HWiNFO or another hardware-monitoring library.

## Development

The project uses the current Macro Deck 3 plugin template, Macro Deck SDK
`3.0.0-beta.15`, and .NET `10.0`. Build and test with:

```powershell
dotnet build .\src\HWiNFODeck\HWiNFODeck.csproj
dotnet test .\tests\HWiNFODeck.Tests\HWiNFODeck.Tests.csproj
```

Create the unsigned development package with the installed Macro Deck tooling:

```powershell
macrodeck-plugin build --source .\src\HWiNFODeck --output .\artifacts
```

The first package is version `0.1.0`. Each release must increment the version in
`src/HWiNFODeck/manifest.json`, which is the plugin's single source of truth for
the version.

## Publishing a release

The `Release` workflow submits a published GitHub release to the Macro Deck
Platform. Keep the release tag in sync with the manifest version (for example,
`v1.0.0`). The approved build appears in the Creator Portal's build library, where
it can be reviewed and released to users.

## Variables

The provider always exposes connection status, HWiNFO version, discovered reading
count, and the initial CPU variables. It also exposes the requested GPU and
motherboard aliases. Every discovered reading is available from the HWiNFO sensor
catalog using a stable `sensor_<sensor-id>_<reading-id>` variable id. Optional
aliases resolve only when a matching HWiNFO reading exists; no zero or synthetic
values are generated.

Additional variables:

- `macrodeck_sdk_version` (text): the Macro Deck SDK version the plugin was built
  against, read from the SDK assembly at runtime (`3.0.0-beta.15` today).
- `macrodeck_version` (text): the Macro Deck 3 version detected from the host
  process that launched the plugin. Populated when the plugin runs under the
  Macro Deck supervisor; unavailable in a plain development run.
- Network transfer speeds, converted from the HWiNFO reading to both megabits
  (`_mbps`) and megabytes (`_mb_s`) per second:
  - `hwinfo_network_download_speed_mbps` / `hwinfo_network_download_speed_mb_s`
  - `hwinfo_network_upload_speed_mbps` / `hwinfo_network_upload_speed_mb_s`

  HWiNFO labels these differently across versions (`Download Speed`,
  `Current DL Rate`, `DL Rate`, ...), so the plugin matches all known labels
  and falls back to any data-rate reading on a network adapter sensor.
  Cumulative `Downloaded Total` readings are never mistaken for speeds.
- Drive storage status:
  - `hwinfo_drive_used_percent` (numeric, %)
  - `hwinfo_drive_free_space_gb` (numeric, GB)

  Resolved from HWiNFO drive readings when present; otherwise from the first
  ready fixed OS drive, since HWiNFO drive sensors rarely expose usage.
- GPU readings, from the first `GPU [#n]` sensor HWiNFO reports:
  - `hwinfo_gpu_name` (text)
  - `hwinfo_gpu_usage` (numeric, %) - `GPU Core Load`, `GPU Utilization` or `GPU D3D Usage`
  - `hwinfo_gpu_temperature`, `hwinfo_gpu_hotspot_temperature`,
    `hwinfo_gpu_memory_junction_temperature` (numeric, °C)
  - `hwinfo_gpu_memory_usage` (numeric, %)
  - `hwinfo_gpu_clock`, `hwinfo_gpu_effective_clock` (numeric, MHz)
  - `hwinfo_gpu_power` (numeric, W)
  - `hwinfo_gpu_fan_rpm` (numeric, RPM)

  Labels are matched exactly, so `GPU Temperature` never resolves to
  `GPU Memory Junction Temperature`, and the fan variable takes the RPM reading
  rather than the duty-cycle percentage HWiNFO publishes under the same label.

HWiNFO Shared Memory must be enabled in HWiNFO. The service polls one shared-memory
connection, retains the last valid reading during transient invalid samples, and
reconnects automatically after HWiNFO exits and restarts.

## Gauge widget

The plugin provides a `gauge` deck widget: a three-quarter arc gauge that fills
as the bound variable climbs toward its maximum. The reading sits in the middle
of the arc, with the unit on its own line under it and the maximum below that.
It is drawn with the Macro Deck UI runtime's own `ui.gauge` component rather than
by hand-placed primitives, so it needs no artwork upload, and falls back to a
range bar on a reader that does not know that component.

It also supports the standard appearance the widget actually uses: **Set
Background Color** and **Set Accent Color** both work, and the accent colour
becomes the filled arc. Macro Deck draws the tile's border as it does for any
widget.

Add it from the widget picker, then open its configuration:

- **Variable** - a variable picker; bind any numeric variable (a HWiNFO reading,
  a user variable, another plugin's variable). HWiNFO readings resolve locally
  in the plugin; everything else resolves through the host.
- **Maximum** - the value at full deflection. The filled fraction scales linearly
  from 0 to `max` and is clamped, so larger values pin at the stop. Shown under
  the reading.
- **Unit** - optional text on its own line under the reading (e.g. `Mbps`, `%`,
  `°C`). Left out entirely when unset.

## Plugin configuration

The gauge refresh interval lives in the plugin configuration, not in the widget:
open the plugin settings and set **Refresh interval (seconds)**
(0.25 to 60, default 1). It applies to every gauge widget immediately. Note
HWiNFO readings themselves update once per second, so sub-second refresh only
helps non-HWiNFO variables.

Because the plugin has a configuration flow, it starts disabled until the
configuration is completed once. Open the plugin settings after installing and
save (the default 1 second is fine) to enable it.

## Removed widgets

The `speedometer` widget type was removed in `0.1.13` and replaced by `gauge`.
Widgets already on a deck keep their stored type and data, so they survive the
upgrade untouched, but they are not served any more and cannot be re-added from
the picker. Delete them from the deck, or downgrade, if you still want them.
