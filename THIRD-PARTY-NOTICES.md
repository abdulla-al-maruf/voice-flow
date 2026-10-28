# Third party notices

The installer bundles the build machine's Node.js runtime. Its full upstream license is in `third-party/NODE-LICENSE.txt`, taken from the matching official Node.js tag. A rebuild with a different Node version must update that file.

The app uses the installed Windows .NET Framework, Windows APIs and system fonts. No font files or third-party sound effects are bundled. Icons are drawn in the source.

The installer bundles `sherpa-onnx-node` and `sherpa-onnx-win-x64` 2.0.1 (Apache-2.0, k2-fsa/sherpa-onnx). They include ONNX Runtime (MIT, Microsoft). They run the offline engine.

The offline Bangla model is not bundled. The app downloads it into `%LOCALAPPDATA%\VoiceFlow\Models` from SayedShaun/bangla-zipformer-onnx on Hugging Face (CC BY-SA 4.0). Its license file is saved next to it.

Sarvam and Chariot are separate services. You use them with your own account and key under their own terms. They are not bundled or sublicensed.
