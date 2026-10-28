# Privacy

Only the engine you picked receives your audio. A paid engine never receives audio unless you chose it.

## Where audio goes

- **Local model:** speech is transcribed on this computer and the audio goes nowhere. The model is downloaded once from huggingface.co (about 71 MB, size and SHA-256 checked) into `%LOCALAPPDATA%\VoiceFlow\Models`.
- **Sarvam and Chariot:** audio goes to that company's servers (`api.sarvam.ai`, `api.chariot.in`). Cost is billed to your own account, and their data policies apply. Your key is stored in `%LOCALAPPDATA%\VoiceFlow\engine-keys.vault`, encrypted with Windows user protection (DPAPI). It is never written to logs or shown on screen. Chariot's connection sends the key as part of the URL, because their documentation says this client cannot set a header.
- If a paid engine fails and the local model is installed, the same recording is re-read once by the local model. In that case the audio does not leave your computer.

## What stays on this computer

- The last 20 recordings (text and audio) are in `%LOCALAPPDATA%\VoiceFlow\History`, encrypted with Windows user protection. This does not hide them from other software running as the same Windows user. Cancel, delete and the 20-item limit remove the app's copies; deleting a file is not forensic secure erasure.
- Logs are in `%LOCALAPPDATA%\VoiceFlow\Logs`: small, rotated, event codes, timestamps, app and Windows versions, counts. No audio, transcripts, clipboard contents, keys, emails or window titles.
- Nothing is uploaded automatically. "Report a problem" only opens the GitHub issue page; whatever you write or attach there is public.
- Recognized text is placed on the system clipboard. A separate clipboard manager keeps its own history, which Voice Flow does not control.
