using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class VoskResultText : MonoBehaviour {
    [Tooltip("VoskSpeechToText component.")]
    public VoskSpeechToText VoskSpeechToText;

    [Tooltip("For displaying results in demo scene.")]
    public Text resultTxt;

    [Tooltip("For displaying partial Speech Recognition results in demo scene.")]
    [TextArea(5, 10)] public string partialResults;

    [Tooltip("Final Speech Recognition results after basic filtering for common audio inputs that are \"duds\".")]
    [TextArea(5, 10)] public string finalResults;

    [Tooltip("Set to true to listen for Speech Audio.")]
    public bool listeningMode;

    [Tooltip("Idle time before Speech Recognition pauses the results queue.")]
    public float idleTimeLimit = 3.333f;

    [Tooltip("Used to filter out \"dud\" entries. Varies by language, but these are the most common false inputs produced by Vosk in English.")]
    public string[] dudEntryStrings = new string[] { "", "the", "hmm", "oh", "uh", "uhh", "um" };

    [Tooltip("Other possible Speech Recognition results, according to the selected Vosk Model.")]
    [TextArea(5, 20)] public string[] resultsAlternatives;

    [Tooltip("Final Speech Recognition results for the last audio input.")]
    [TextArea(3, 5)] public List<string> allResults;

    [Tooltip("Set to true to see process debug messages as they happen.")]
    public bool debug;

    void Awake() {
        VoskSpeechToText.OnTranscriptionResult += OnTranscriptionResult;
    }

    private void Update() {
        if (!VoskSpeechToText._recognizerReady) {
            return;
        }
        if (!listeningMode && VoskSpeechToText._running || listeningMode && !VoskSpeechToText._running) {
            VoskSpeechToText.ToggleRecording();
        }
        if (!string.IsNullOrEmpty(VoskSpeechToText.partialResult)) {
            RecognitionResult _partialResult = new RecognitionResult(VoskSpeechToText.partialResult);
            if (_partialResult.Phrases[0].Text.Length > 0) {
                LogPartialResult(_partialResult.Phrases[0].Text);
            }
        }
    }

    /// <summary>
    /// Callback for VoskSpeechToText.OnTranscriptionResult.
    /// </summary>
    /// <param name="obj"></param>
    private void OnTranscriptionResult( string obj ) {
        if (debug) { Debug.Log(obj); }
        
        var result = new RecognitionResult(obj);
        resultsAlternatives = new string[result.Phrases.Length];
        if (resultsAlternatives.Length > 0) {
            ProcessAndFilterResults(result);
        }
    }

    /// <summary>
    /// Attempts to filter out "dud" entries from the Speech Recognition results.
    /// </summary>
    /// <param name="_result">The RecognitionResult from Vosk's callbacks.</param>
    private void ProcessAndFilterResults( RecognitionResult _result ) {
        bool[] dudEntries = new bool[_result.Phrases.Length];
        int count = 0;
        for (int i = 0; i < _result.Phrases.Length; i++) {
            resultsAlternatives[i] = _result.Phrases[i].Text;
            for (int j = 0; j < dudEntryStrings.Length; j++) {
                if (resultsAlternatives[i] == dudEntryStrings[j]) {
                    dudEntries[i] = true;
                    count++;
                    break;
                }
            }
        }
        if (resultsAlternatives.Length > 0 && count < resultsAlternatives.Length) {
            for (int i = 0; i < resultsAlternatives.Length; i++) {
                if (!dudEntries[i]) {
                    finalResults = resultsAlternatives[i];
                    string[] resultStrArr = finalResults.Split(' ');
                    bool[] _dudEntries = new bool[resultStrArr.Length];
                    for (int j = 0; j < resultStrArr.Length; j++) {
                        for (int k = 0; k < dudEntryStrings.Length; k++) {
                            if (resultStrArr[j] == dudEntryStrings[k]) {
                                if (debug) { Debug.Log(resultStrArr[j] + " == " + dudEntryStrings[k]); }                                
                                _dudEntries[j] = true;
                            }
                        }
                    }
                    if (_dudEntries[0]) {
                        resultStrArr[0] = "";
                    }
                    if (resultStrArr.Length > 1 && _dudEntries[resultStrArr.Length - 1]) {
                        resultStrArr[resultStrArr.Length - 1] = "";
                        finalResults = "";
                        partialResults = "";
                    }
                    if (resultTxt != null) {
                        resultTxt.text = finalResults;
                    }
                }
            }
            allResults.Insert(0, string.Join("\n", resultsAlternatives));

        } else {
            finalResults = "";
        }

    }

    /// <summary>
    /// Logs partial Speech Recognition results as they come in.
    /// </summary>
    /// <param name="_partialStr"></param>
    public void LogPartialResult( string _partialStr ) {
        partialResults = _partialStr;
        if (resultTxt != null) { 
            resultTxt.text = partialResults;
        }
    }

    private void OnApplicationQuit() {
        VoskSpeechToText.OnTranscriptionResult -= OnTranscriptionResult;
    }
}
