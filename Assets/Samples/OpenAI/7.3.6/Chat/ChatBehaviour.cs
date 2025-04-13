// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Images;
using OpenAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using Utilities.Audio;
using Utilities.Encoding.Wav;
using Utilities.Extensions;
using Utilities.WebRequestRest;

namespace OpenAI.Samples.Chat
{
    public class ChatBehaviour : MonoBehaviour
    {
        public GameObject talkBall1;
        public GameObject talkBall2;
        public GameObject talkBall3;

        public Animator anim;
        public SkinnedMeshRenderer skinnedMeshRenderer;

        private float blinkTimer = 3f;
        private bool blinkOn = false;
        private float blinkValue = 0f;

        [SerializeField]
        private OpenAIConfiguration configuration;

        [SerializeField]
        private bool enableDebug;

        [SerializeField]
        private Button submitButton;

        [SerializeField]
        private Button recordButton;

        [SerializeField]
        private TMP_InputField inputField;

        [SerializeField]
        private RectTransform contentArea;

        [SerializeField]
        private ScrollRect scrollView;

        
        public AudioSource audioSource;


        private string systemPrompt = "Act as Socrates. Your name is Socrates. Answer questions as Socrates. Give your answers in English.";
        private int messageUpperLimit = 9;

        private OpenAIClient openAI;

        private List<Message> messages = new List<Message>();

        private CancellationTokenSource lifetimeCancellationTokenSource;

        private void SetBall(int ball)
        {
            talkBall1.transform.localPosition = new Vector3(-1000, -1000, -1000);
            talkBall2.transform.localPosition = new Vector3(-1000, -1000, -1000);
            talkBall3.transform.localPosition = new Vector3(-1000, -1000, -1000);

            switch (ball)
            {
                case 1:
                    talkBall1.transform.localPosition = new Vector3(0, 0, 0);
                    break;
                case 2:
                    talkBall2.transform.localPosition = new Vector3(0, 0, 0);
                    break;
                case 3:
                    talkBall3.transform.localPosition = new Vector3(0, 0, 0);
                    break;
            }
        }
        bool enableToggle = true;

        bool prevTriggerValue = false;

        private void LateUpdate()
        {
            if (enableToggle)
            {
                var inputDevices = new List<UnityEngine.XR.InputDevice>();
                UnityEngine.XR.InputDevices.GetDevices(inputDevices);

                foreach (var device in inputDevices)
                {

                    if (device.name.Contains("Quest"))
                    {
                        bool presenceOn;
                        device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out presenceOn);
                        if (!presenceOn)
                        {
                            List<Message> messagesNew = new List<Message>();
                            messagesNew.Add(new Message(Role.System, systemPrompt));
                            messages = messagesNew;
                        }
                    }

                    if (device.name.Contains("Right")) {

                        bool triggerValue;

                        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out triggerValue))
                        {
                            if (triggerValue != prevTriggerValue)
                            {
                                // just changed state
                                ToggleRecording();
                                if (triggerValue)
                                {
                                    SetBall(2);
                                }
                            }

                            prevTriggerValue = triggerValue;
                        }
                    }
                }
            }
            else
            {
                toggleTimer -= Time.deltaTime;
                if(toggleTimer < 0)
                {
                    enableToggle = true;
                    SetBall(1);
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                ToggleRecording();
            }

            blinkTimer -= Time.deltaTime;
            if(blinkTimer < 0)
            {
                if(blinkOn)
                {
                    blinkValue -= Time.deltaTime * 500;
                    skinnedMeshRenderer.SetBlendShapeWeight(6, Math.Clamp(blinkValue,0,100));
                    if(blinkValue < 0)
                    {
                        blinkOn = false;
                        blinkTimer = UnityEngine.Random.Range(3f, 7f);
                    }
                }
                else
                {
                    blinkValue += Time.deltaTime * 500;
                    skinnedMeshRenderer.SetBlendShapeWeight(6, Math.Clamp(blinkValue, 0, 100));

                    if (blinkValue > 100)
                    {
                        blinkOn = true;
                    }
                }
            }
        }

        private void OnValidate()
        {
            inputField.Validate();
            contentArea.Validate();
            submitButton.Validate();
            recordButton.Validate();
            audioSource.Validate();
        }
        private void Awake()
        {
            SetBall(1);

            OnValidate();
            lifetimeCancellationTokenSource = new CancellationTokenSource();
            openAI = new OpenAIClient(configuration)
            {
                EnableDebug = enableDebug
            };
            messages.Add(new Message(Role.System, systemPrompt));
            inputField.onSubmit.AddListener(SubmitChat);
            submitButton.onClick.AddListener(SubmitChat);
            recordButton.onClick.AddListener(ToggleRecording);
        }

        private void OnDestroy()
        {
            lifetimeCancellationTokenSource.Cancel();
            lifetimeCancellationTokenSource.Dispose();
            lifetimeCancellationTokenSource = null;
        }

        private void SubmitChat(string _) => SubmitChat();

        private static bool isChatPending;

        private async void SubmitChat()
        {
            if (isChatPending || string.IsNullOrWhiteSpace(inputField.text)) { return; }
            isChatPending = true;

            inputField.ReleaseSelection();
            inputField.interactable = false;
            submitButton.interactable = false;
            messages.Add(new Message(Role.User, inputField.text));
            var userMessageContent = AddNewTextMessageContent(Role.User);
            userMessageContent.text = $"User: {inputField.text}";
            inputField.text = string.Empty;
            var assistantMessageContent = AddNewTextMessageContent(Role.Assistant);
            assistantMessageContent.text = "Socrates: ";

            try
            {
                var request = new ChatRequest(messages);
                var response = await openAI.ChatEndpoint.StreamCompletionAsync(request, resultHandler: deltaResponse =>
                {
                    if (deltaResponse?.FirstChoice?.Delta == null) { return; }
                    assistantMessageContent.text += deltaResponse.FirstChoice.Delta.ToString();
                    scrollView.verticalNormalizedPosition = 0f;
                }, lifetimeCancellationTokenSource.Token);

                messages.Add(response.FirstChoice.Message);

                // to limit to much usage, if we exceed upper limit, just use last 6 messages
                if (messages.Count > messageUpperLimit) {
                    Debug.Log("Limit: " + messages);
                    List<Message> messagesNew = new List<Message>();
                    messagesNew.Add(new Message(Role.System, systemPrompt));
                    
                    int startIndex = Math.Max(messages.Count - messageUpperLimit + 1, 0);
                    
                    for (int i = startIndex; i < messages.Count; i++) {
                        messagesNew.Add(messages[i]);
                    }

                    messages = messagesNew;
                }

                GenerateSpeech(response.ToString());
            }
            catch (Exception e)
            {
                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogError(e);
                        break;
                }
            }
            finally
            {
                if (lifetimeCancellationTokenSource is { IsCancellationRequested: false })
                {
                    inputField.interactable = true;
                    submitButton.interactable = true;
                }

                isChatPending = false;
            }
        }

        private void OnClipEndAnimate()
        {
            anim.SetInteger("AnimNo", 0);
        }

        private async void GenerateSpeech(string text)
        {
            var request = new SpeechRequest(text, Model.TTS_1, SpeechVoice.Onyx);
            var (clipPath, clip) = await openAI.AudioEndpoint.CreateSpeechAsync(request, lifetimeCancellationTokenSource.Token);
            audioSource.clip = clip;
            
            audioSource.Play();
            anim.SetInteger("AnimNo", UnityEngine.Random.Range(1,7));

            enableToggle = true;
            SetBall(1);

            Invoke("OnClipEndAnimate", clip.length);

            if (enableDebug)
            {
                Debug.Log(clipPath);
            }
        }

        private TextMeshProUGUI AddNewTextMessageContent(Role role)
        {
            var textObject = new GameObject($"{contentArea.childCount + 1}_{role}");
            textObject.transform.SetParent(contentArea, false);
            var textMesh = textObject.AddComponent<TextMeshProUGUI>();
            textMesh.fontSize = 24;
#if UNITY_2023_1_OR_NEWER
            textMesh.textWrappingMode = TextWrappingModes.Normal;
#else
            textMesh.enableWordWrapping = true;
#endif
            return textMesh;
        }

        private void AddNewImageContent(Texture2D texture)
        {
            var imageObject = new GameObject($"{contentArea.childCount + 1}_Image");
            imageObject.transform.SetParent(contentArea, false);
            var rawImage = imageObject.AddComponent<RawImage>();
            rawImage.texture = texture;
            var layoutElement = imageObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = texture.height / 4f;
            layoutElement.preferredWidth = texture.width / 4f;
            var aspectRatioFitter = imageObject.AddComponent<AspectRatioFitter>();
            aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            aspectRatioFitter.aspectRatio = texture.width / (float)texture.height;
        }
        private async Task<ImageResult> GenerateImageAsync(ImageGenerationRequest request)
        {
            var results = await openAI.ImagesEndPoint.GenerateImageAsync(request);
            return results.FirstOrDefault();
        }

        private float toggleTimer = 0;

        private void ToggleRecording()
        {
            RecordingManager.EnableDebug = enableDebug;

            if (RecordingManager.IsRecording)
            {
                RecordingManager.EndRecording();
                SetBall(3);
                enableToggle = false;
                toggleTimer = 15;
            }
            else
            {
                inputField.interactable = false;
                RecordingManager.StartRecording<WavEncoder>(callback: ProcessRecording);
            }
        }

        private async void ProcessRecording(Tuple<string, AudioClip> recording)
        {
            var (path, clip) = recording;

            if (enableDebug)
            {
                Debug.Log(path);
            }

            try
            {
                recordButton.interactable = false;
                var request = new AudioTranscriptionRequest(clip, language: "tr");
                var userInput = await openAI.AudioEndpoint.CreateTranscriptionAsync(request, lifetimeCancellationTokenSource.Token);

                if (enableDebug)
                {
                    Debug.Log(userInput);
                }

                inputField.text = userInput;
                SubmitChat();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                inputField.interactable = true;
            }
            finally
            {
                recordButton.interactable = true;
            }
        }
    }
}
