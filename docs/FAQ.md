# FAQ

## Which engine should I use?

Start with the **local model**: it is free and needs the internet only once. If you want an online engine, try **Sarvam** or **Chariot** with your own key and compare on your own voice. Accuracy depends on your microphone, accent and how you speak, so test with sentences you actually say.

## Is my audio uploaded?

Only to the engine you picked. With the local model it never leaves your computer. See [PRIVACY.md](PRIVACY.md).

## Does it work offline?

Yes, with the local model after its one-time download.

## Does it cost money?

Voice Flow is free. The local model is free. Sarvam and Chariot charge under their own terms to your own account; check their pricing and any free credit on their websites, because these can change.

## Why is there no Google engine?

An earlier private build could use a Google account sign-in. That part relies on another company's sign-in and is not included in this public repository or the installer.

## Can I use it in any app?

It types into whichever text field has the cursor, using normal Windows input. Some apps with unusual input handling may not accept it; the text then stays on the clipboard.

## How many recordings are kept?

The last 20, text and audio, encrypted for your Windows user.

## Where are my files and keys?

Under `%LOCALAPPDATA%\VoiceFlow`. Keys and history are encrypted with Windows DPAPI.

## How do I report a bug or ask for a feature?

Open an issue on GitHub. Do not include keys, passwords or private audio.
