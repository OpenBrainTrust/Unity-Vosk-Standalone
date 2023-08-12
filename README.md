# Unity-Vosk-Standalone
## What's Vosk?
 Vosk is a free, open-source solution for Speech-To-Text / Automatic Speech Recognition / STT / ASR / whatever you like to call it. Vosk has models for many common languages. See the page here for a list of supported languages: https://alphacephei.com/vosk/models
 
## They Aleady Have A Unity Implementation Though
 While Vosk is fairly performant "out of the box", it has issues with room noise and microphone volume; this often results in "dud" words coming in. 
 
 Typically, room noise detected from the microphone during listening will either send a single word in periodically, or put the same word at the beginning and end of the text of sentences spoken. 
 
## Dud Example
 You say "I'm going to the store."
 Vosk prints "the I'm going to the store the."
 
 As you can imagine, this is frustrating.

## What We Did About It 
 This modified Vosk for Unity implementation provides some Speech Recognition results filtering for common audio misinterpretations above the default to eliminate those as best as possible.
 
 The array that stores the common "dud" words or phrases can be altered to suit whatever Speech Recognition Model you're using. We assume the "dud" words would be different in other languages.
 
 The code has been better commented, or tooltipped in the case of public variables in the Unity Inspector. Some of the existing parameters were exposed so you can see the components at work.
 
 Prefab for Vosk included.
 
 Scene with the Prefab in situ also included.
 
 The aim was to squeeze a bit more efficiency out of the default Vosk without writing an entire plug-in layer on top for error correction, and without going and buying Recognissimo (good plug-in if you want to spend a bit of money).

## No Model In This Repository!
 This repo doesn't contain a Vosk model (bloat, also too many options, you should go pick). So again, you'll want to grab a Vosk Model from AlphaCephei at the link above.
 
## Where To Put The Model?
 
 Place downloaded Vosk Models in the StreamingAssets folder of your Unity Project. Target the model with the ModelPath parameter in the VoskSpeechToText component / script.

## How To Change The Dud Word Filter?
 Change dud words to filter out in the VoskResultText component / script with the dudEntryStrings parameter.
 
## Notes:
 The .UnityPackage is provided as a convenience. The source and files in the Assets folders are the same.
 
 Acknowledgements to Ionic.zip, works better than System.IO.Compression in this case. If you already have Ionic.zip in your project, don't copy the ThirdParty folder in.
 