using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ionic.Zip;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Networking;
using Vosk;

/// <summary>
/// Slightly modified from AlphaCephei's Vosk Unity Demo. Provides some underlying MonoBehaviour UI functionality.
/// </summary>
public class VoskSpeechToText : MonoBehaviour {
    [Tooltip("Location of the model, relative to the Streaming Assets folder. Go to https://alphacephei.com/vosk/models to download relevant ASR models.")]
    public string ModelPath = "LanguageModels/vosk-model-en-us-0.22-lgraph.zip";

    [Tooltip("The source of the microphone input.")]
    public VoiceProcessor VoiceProcessor;

    [Tooltip("The Max number of alternatives that will be processed.")]
    public int MaxAlternatives = 3;

    [Tooltip("How long should we record before restarting?")]
    public float MaxRecordLength = 6;

    [Tooltip("Should the recognizer start when the application is launched?")]
    public bool AutoStart = true;

    [TextArea(5, 10)] public string partialResult;

    [Tooltip("The phrases that will be detected. If left empty, all words will be detected.")]
    public List<string> KeyPhrases = new List<string>();

    //Cached version of the Vosk Model.
    private Model _model;

    //Cached version of the Vosk recognizer.
    private VoskRecognizer _recognizer;

    [Tooltip("Conditional flag to see if a recognizer has already been created.")]
    public bool _recognizerReady;

    //Holds all of the audio data until the user stops talking.
    private readonly List<short> _buffer = new List<short>();

    //Called when the the state of the controller changes.
    public Action<string> OnStatusUpdated;

    //Called after the user is done speaking and vosk processes the audio.
    public Action<string> OnTranscriptionResult;

    [Tooltip("The absolute path to the decompressed model folder.")]
    public string _decompressedModelPath;

    //A string that contains the keywords in Json Array format
    private string _grammar = "";

    [Tooltip("True if the Model is decompressing.")]
    public bool _isDecompressing;

    [Tooltip("True if Vosk is still initializing.")]
    public bool _isInitializing;

    [Tooltip("True if Vosk started successfully.")]
    public bool _didInit;

    [Tooltip("True if Vosk is running.")]
    public bool _running;

    //Thread safe queue of microphone data.
    private readonly ConcurrentQueue<short[]> _threadedBufferQueue = new ConcurrentQueue<short[]>();

    //Thread safe queue of resuts
    private readonly ConcurrentQueue<string> _threadedResultQueue = new ConcurrentQueue<string>();

    static readonly ProfilerMarker voskRecognizerCreateMarker = new ProfilerMarker("VoskRecognizer.Create");
    static readonly ProfilerMarker voskRecognizerReadMarker = new ProfilerMarker("VoskRecognizer.AcceptWaveform");

    [Tooltip("Set to true for Debug messages.")]
    bool debug = false;

    //If Auto start is enabled, starts vosk speech to text.
    void Start() {
        if (AutoStart) {
            StartVoskStt();
        }
    }

    /// <summary>
    /// Start Vosk Speech to text
    /// </summary>
    /// <param name="keyPhrases">A list of keywords/phrases. Keywords need to exist in the models dictionary, so some words like "webview" are better detected as two more common words "web view".</param>
    /// <param name="modelPath">The path to the model folder relative to StreamingAssets. If the path has a .zip ending, it will be decompressed into the application data persistent folder.</param>
    /// <param name="startMicrophone">"Should the microphone after Vosk initializes?</param>
    /// <param name="maxAlternatives">The maximum number of alternative phrases detected</param>
    public void StartVoskStt( List<string> keyPhrases = null, string modelPath = default, bool startMicrophone = false, int maxAlternatives = 3 ) {
        if (_isInitializing) {
            if (debug) {
                Debug.LogError("Initializing in progress!");
            }
            return;
        }
        if (_didInit) {
            if (debug) {
                Debug.LogError("Vosk has already been initialized!");
            }
            return;
        }

        if (!string.IsNullOrEmpty(modelPath)) {
            ModelPath = modelPath;
        }

        if (keyPhrases != null) {
            KeyPhrases = keyPhrases;
        }

        MaxAlternatives = maxAlternatives;
        StartCoroutine(DoStartVoskStt(startMicrophone));
    }

    //Decompress model, load settings, start Vosk and optionally start the microphone
    private IEnumerator DoStartVoskStt( bool startMicrophone ) {
        _isInitializing = true;
        yield return WaitForMicrophoneInput();

        yield return Decompress();

        OnStatusUpdated?.Invoke("Loading Model from: " + _decompressedModelPath);
        //Vosk.Vosk.SetLogLevel(0);
        _model = new Model(_decompressedModelPath);

        yield return null;

        OnStatusUpdated?.Invoke("Initialized");
        VoiceProcessor.OnFrameCaptured += VoiceProcessorOnOnFrameCaptured;
        VoiceProcessor.OnRecordingStop += VoiceProcessorOnOnRecordingStop;

        if (startMicrophone)
            VoiceProcessor.StartRecording();

        _isInitializing = false;
        _didInit = true;

        ToggleRecording();
    }

    //Translates the KeyPhrases into a json array and appends the `[unk]` keyword at the end to tell Vosk to filter other phrases.
    private void UpdateGrammar() {
        if (KeyPhrases.Count == 0) {
            _grammar = "";
            return;
        }

        SimpleJSONArray keywords = new SimpleJSONArray();
        foreach (string keyphrase in KeyPhrases) {
            keywords.Add(new SimpleJSONString(keyphrase.ToLower()));
        }

        keywords.Add(new SimpleJSONString("[unk]"));

        _grammar = keywords.ToString();
    }

    //Decompress the model zip file or return the location of the decompressed files.
    private IEnumerator Decompress() {
        if (!Path.HasExtension(ModelPath) || Directory.Exists(
                Path.Combine(Application.streamingAssetsPath,
                Path.GetFileNameWithoutExtension(ModelPath)))
            ) {
            OnStatusUpdated?.Invoke("Using existing decompressed model.");
            _decompressedModelPath =
                Path.Combine(Application.streamingAssetsPath, Path.GetFileNameWithoutExtension(ModelPath));
            if (debug) {
                Debug.Log(_decompressedModelPath);
            }
            yield break;
        }

        OnStatusUpdated?.Invoke("Decompressing model...");
        string dataPath = Path.Combine(Application.streamingAssetsPath, ModelPath);

        Stream dataStream;
        // Read data from the streaming assets path. You cannot access the streaming assets directly on Android.
        if (dataPath.Contains("://")) {
            UnityWebRequest www = UnityWebRequest.Get(dataPath);
            www.SendWebRequest();
            while (!www.isDone) {
                yield return null;
            }
            dataStream = new MemoryStream(www.downloadHandler.data);
        }
        // Read the file directly on valid platforms.
        else {
            dataStream = File.OpenRead(dataPath);
        }

        //Read the Zip File

        var zipFile = Ionic.Zip.ZipFile.Read(dataStream);

        //Listen for the zip file to complete extraction
        zipFile.ExtractProgress += ZipFileOnExtractProgress;

        //Update status text
        OnStatusUpdated?.Invoke("Reading Zip file");

        //Start Extraction
        zipFile.ExtractAll(Application.streamingAssetsPath);

        //Wait until it's complete
        while (_isDecompressing == false) {
            yield return null;
        }
        //Override path given in ZipFileOnExtractProgress to prevent crash
        _decompressedModelPath = Path.Combine(Application.streamingAssetsPath, Path.GetFileNameWithoutExtension(ModelPath));

        //Update status text
        OnStatusUpdated?.Invoke("Decompressing complete!");
        //Wait a second in case we need to initialize another object.
        yield return new WaitForSeconds(1);
        //Dispose the zipfile reader.
        zipFile.Dispose();
    }

    ///The function that is called when the zip file extraction process is updated.
    private void ZipFileOnExtractProgress( object sender, ExtractProgressEventArgs e ) {
        if (e.EventType == ZipProgressEventType.Extracting_AfterExtractAll) {
            _isDecompressing = true;
            _decompressedModelPath = e.ExtractLocation;
        }
    }

    //Wait until microphones are initialized
    private IEnumerator WaitForMicrophoneInput() {
        while (Microphone.devices.Length <= 0)
            yield return null;
    }

    /// <summary>
    /// Toggles recording / listening for voice input.
    /// </summary>
    public void ToggleRecording() {
        if (debug) {
            Debug.Log("Toogle Recording");
        }
        if (!VoiceProcessor.IsRecording) {
            if (debug) {
                Debug.Log("Start Recording");
            }
            _running = true;
            VoiceProcessor.StartRecording();
            Task.Run(ThreadedWork).ConfigureAwait(false);
        } else {
            if (debug) {
                Debug.Log("Stop Recording");
            }
            _running = false;
            VoiceProcessor.StopRecording();
        }
    }

    //Calls the On Phrase Recognized event on the Unity Thread
    void Update() {
        if (_threadedResultQueue.TryDequeue(out string voiceResult)) {
            OnTranscriptionResult?.Invoke(voiceResult);
        }
    }

    //Callback from the voice processor when new audio is detected
    private void VoiceProcessorOnOnFrameCaptured( short[] samples ) {
        _threadedBufferQueue.Enqueue(samples);
    }

    //Callback from the voice processor when recording stops
    private void VoiceProcessorOnOnRecordingStop() {
        if (debug) {
            Debug.Log("Stopped");
        }
    }

    //Feeds the autio logic into the vosk recorgnizer
    private async Task ThreadedWork() {
        voskRecognizerCreateMarker.Begin();
        if (!_recognizerReady) {
            UpdateGrammar();

            //Only detect defined keywords if they are specified.
            if (string.IsNullOrEmpty(_grammar)) {
                _recognizer = new VoskRecognizer(_model, 16000.0f);
            } else {
                _recognizer = new VoskRecognizer(_model, 16000.0f, _grammar);
            }

            _recognizer.SetMaxAlternatives(MaxAlternatives);
            //_recognizer.SetWords(true);
            _recognizerReady = true;

            if (debug) {
                Debug.Log("Recognizer ready");
            }
        }

        voskRecognizerCreateMarker.End();

        voskRecognizerReadMarker.Begin();

        while (_running) {
            if (_threadedBufferQueue.TryDequeue(out short[] voiceResult)) {
                if (_recognizer.AcceptWaveform(voiceResult, voiceResult.Length)) {
                    var result = _recognizer.Result();
                    _threadedResultQueue.Enqueue(result);

                } else {
                    var partial = _recognizer.PartialResult();
                    partialResult = partial;
                }
            } else {
                // Wait for some data
                await Task.Delay(100);
            }
        }

        voskRecognizerReadMarker.End();
    }
}
