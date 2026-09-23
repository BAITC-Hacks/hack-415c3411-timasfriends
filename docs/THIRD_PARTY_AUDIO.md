# Third-party interface audio

QuestBridge includes four sounds from **Interface Sounds 1.0**, created by **Kenney** (2020).

- Original asset page: https://kenney.nl/assets/interface-sounds
- Author's OpenGameArt release: https://opengameart.org/content/interface-sounds
- Downloaded archive: https://opengameart.org/sites/default/files/kenney_interfaceSounds.zip
- License: **Creative Commons Zero (CC0 1.0)** — https://creativecommons.org/publicdomain/zero/1.0/
- Downloaded on: 2026-09-23.
- The author's original `License.txt` is preserved as `Assets/QuestBridge/Resources/Audio/Kenney-License.txt`.

The official Kenney page and the author's OpenGameArt upload both identify the pack as CC0. The archive was downloaded from OpenGameArt because the local connection to the kenney.nl archive failed. Attribution is appreciated by the author but is not required by CC0.

## Included files

Files are renamed for convenient Unity Resources loading; audio content is unchanged. All are mono Ogg Vorbis at 44.1 kHz. Total audio size is approximately 33 KB.

| Local resource path | Original archive path | Length | Suggested use |
|---|---|---:|---|
| `Audio/Click` | `Audio/click_002.ogg` | 0.010 s | Short button press feedback |
| `Audio/Open` | `Audio/select_003.ogg` | 0.383 s | Open a task or team profile |
| `Audio/Rise` | `Audio/maximize_007.ogg` | 0.186 s | A card moves upward in the ranking |
| `Audio/Leader` | `Audio/confirmation_002.ogg` | 0.539 s | New leader or successful publication |

## Playback guidance

Use short 2D one-shot playback with a visible mute control. Start with a low gain (around 0.10 for clicks and 0.15–0.20 for prominent events), then tune by listening in the running application. Coalesce rank updates into one sound and avoid playing a sound on every polling request or scroll frame. Do not play a leader sound merely because the same top card was returned again. Web browsers can require a user gesture before allowing audio.

No music or large sound library is included. File headers and duration were checked; subjective loudness and musical fit should be judged in the running Unity scene.
