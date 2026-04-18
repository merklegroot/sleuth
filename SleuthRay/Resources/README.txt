Embedded data (compiled into SleuthRay.dll)
--------------------------------------------

- wanderer_talk.json — NPC dialogue (see Program.cs / WandererTalk).
- gunshot.wav — gunshot SFX. Replace this file in the repo to change the in-game sound, then rebuild.
  The committed clip is a short **synthetic** burst (brown noise + filters), not a real recording.
  For Dan Sfx’s pack, extract your preferred WAV and overwrite Resources/gunshot.wav (keep the filename).

Optional: set environment variable SLEUTHRAY_GUNSHOT_WAV to the full path of an external WAV to override
the embedded clip at runtime (no rebuild).

See repo ATTRIBUTION.md for third-party audio credit.
