Embedded data (compiled into SleuthRay.dll)
--------------------------------------------

- wanderer_talk.json — NPC dialogue (see Program.cs / WandererTalk).
- mixkit-sweet-kitty-meow-93-trimmed.wav — shot SFX (currently a cat meow). Replace this file in the repo to
  change the in-game sound, then rebuild.

Optional: set environment variable SLEUTHRAY_GUNSHOT_WAV to the full path of an external WAV to override
the embedded clip at runtime (no rebuild).

See repo ATTRIBUTION.md for third-party audio credit.
