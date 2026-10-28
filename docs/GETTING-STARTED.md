# Getting started

This guide takes you from download to your first dictated sentence. It assumes nothing. Plan on about 10 minutes, most of it the one-time model download.

## 1. Install

1. Open the [latest release](https://github.com/abdulla-al-maruf/voice-flow/releases/latest) and download `VoiceFlow-0.3.3-Setup.exe`.
2. Run it. If Windows shows a blue "Windows protected your PC" box, the installer is not code-signed yet. Click **More info**, then **Run anyway**. If you want to be careful, first compare the file's SHA-256 with `SHA256SUMS.txt` on the release page (`Get-FileHash .\VoiceFlow-0.3.3-Setup.exe` in PowerShell).
3. Click the install button and wait. Voice Flow opens when it finishes.

Requirements: Windows 10 or 11 (64-bit) and .NET Framework 4.7.2 or newer (already present on current Windows). Node.js is bundled; you do not install it.

## 2. Pick an engine

The main window has a **voice engine** box with a drop-down. Start with the local model.

### Local model (free, works offline after one download)

1. Choose **Local model** in the drop-down.
2. Press the black download button ("লোকাল মডেল নামান"). It downloads about 71 MB once and checks the files. You need internet only for this step.
3. When the status turns green ("✓ প্রস্তুত", ready), you are done.

### Sarvam or Chariot (your own account and key)

These are online services. You use your own account, and they bill you under their own prices.

1. Choose **Sarvam** or **Chariot** in the drop-down.
2. Press the yellow **"চাবি নিন"** (Get key) button. The provider's key page opens in your browser. Sign in or create an account there.
3. Create an API key and copy it.
4. Back in Voice Flow, press **"চাবি বসান"** (Paste key), paste the key, and save. For Chariot you can also press the verify button to check the key.

The key is stored encrypted on your computer. You can replace or remove it from the same dialog.

## 3. Your first dictation

1. Click where you want text, for example a Notepad window.
2. Press **Ctrl+Space**. A small caption card appears and Voice Flow listens.
3. Speak in Bangla, English, or both mixed.
4. Press **Ctrl+Space** again. The text appears at your cursor.
5. Press **Esc** instead of the second Ctrl+Space to cancel.

If another app already uses Ctrl+Space, Voice Flow picks a fallback shortcut and shows it in the main window.

## 4. Everyday use

- **Recent** ("সাম্প্রতিক কথা") opens your last 20 recordings. You can play, copy, retry or delete them.
- Closing the main window keeps Voice Flow in the system tray. Use the tray icon menu to quit.
- If text could not be inserted, it is on your clipboard. Press Ctrl+V.
- The "···" menu has Report a problem, Save log, Check for updates and Quit.

## 5. If something fails

- A paid engine fails and the local model is installed: the same recording is read again by the local model, and the caption says so.
- No text appears: see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
- Microphone not working: Windows Settings, Privacy and security, Microphone. Allow desktop apps.

## 6. Uninstall

Windows Settings, Apps, Installed apps, Voice Flow, Uninstall. The uninstaller can also remove your local history.

## Stuck? Ask an AI assistant

Paste this into any AI assistant (Claude, ChatGPT, Gemini) together with the repository link:

> I want to install and use Voice Flow from https://github.com/abdulla-al-maruf/voice-flow on Windows. Read the README and the docs folder, then walk me through it step by step. I am on Windows [10 or 11]. My problem is: [describe it].

The docs are written so an assistant can follow them. Never paste your API key into a chat; paste it only into the Voice Flow key dialog.
