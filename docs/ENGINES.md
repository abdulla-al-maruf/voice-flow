# Voice engines

Pick the engine in the main window. The app shows the numbered steps for the one you pick. The full walkthrough is in [GETTING-STARTED.md](GETTING-STARTED.md).

| | Local model | Sarvam | Chariot |
|---|---|---|---|
| Internet | Once, to download | Every recording | Every recording |
| Cost | Free | Your own account | Your own account |
| Audio leaves your PC | No | Yes, to Sarvam | Yes, to Chariot |
| Setup | Download button (about 71 MB) | Get key, paste key | Get key, paste key |

## Local model (offline)

1. Press the download button. About 71 MB, once. Internet is needed only for this step.
2. After that it works with no internet and audio never leaves your computer.

Model: SayedShaun/bangla-zipformer-onnx on Hugging Face (CC BY-SA 4.0), run with sherpa-onnx. The files are checked by size and SHA-256 after download.

## Sarvam

1. Press "চাবি নিন" (Get key). Sarvam's key page (`indus.sarvam.ai/key-management`) opens in your browser.
2. Sign in, create an API key, copy it.
3. Press "চাবি বসান" (Paste key), paste, save.

Audio is sent to Sarvam and billed to your account.

## Chariot

1. Press "চাবি নিন". Chariot's site (`platform.chariot.in`) opens.
2. Sign in, create an API key, copy it.
3. Press "চাবি বসান", paste, save. The verify button checks the key against Chariot's credit endpoint, which is free.

## Switching engines and removing keys

Use the drop-down in the main window. To replace or remove a key, open the same key dialog and use the buttons there.

## When a paid engine fails

If Sarvam or Chariot fails (wrong key, no credit, no internet) and the local model is installed, the same recording is read once by the local model and the caption card says so. If the local model is not installed, the recording is kept in history so you can retry.

## Notes

- Keys are encrypted with Windows DPAPI and never logged.
- Prices, free credit and language support are set by those companies and can change.
