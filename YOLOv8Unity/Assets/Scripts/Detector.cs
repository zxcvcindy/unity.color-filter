using Assets.Scripts;
using Assets.Scripts.TextureProviders;
using NN;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Unity.Barracuda;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class Detector : MonoBehaviour
/*
使用 MonoBehaviour，意味著你的腳本是 附加在某個 GameObject 上的元件 (Component)，
並且可以藉由 Unity 的生命週期函式 (如 Start、Update) 來控制行為，若Awake() 沒有實作也會正常走後續流程。
*/
{
    [Tooltip("File of YOLO model.")]
    [SerializeField]
    protected NNModel ModelFile;

    [Tooltip("RawImage component which will be used to draw resuls.")]
    [SerializeField]
    protected RawImage ImageUI;

    [Range(0.0f, 1f)]
    [Tooltip("The minimum value of box confidence below which boxes won't be drawn.")]
    [SerializeField]
    protected float MinBoxConfidence = 0.5f;

    [SerializeField]
    protected TextureProviderType.ProviderType textureProviderType;//TextureProviderType.ProviderType在TextureProvider.cs

    [SerializeReference]
    protected TextureProvider textureProvider = null;

    protected NNHandler nn;
    protected Color[] colorArray = new Color[] { Color.red };

    [SerializeField] Shader colorBlindShader;
    //[SerializeField, Range(0f, 1f)] float colorBlindIntensity = 0.7f;
    [SerializeField] int colorBlindMode = 0; // 0=prot,1=deut,2=tri

    Material colorBlindMaterial;
    // Material 是 Unity 內建的 UnityEngine.Material 類別，不需要額外安裝。它用來包裝 shader 並存放每個實例的參數，然後掛在 Renderer/RawImage 等元件上。
    readonly Vector4[] boxBuffer = new Vector4[64];
    int boxCount;
    YOLOv8 yolo;
    RenderTexture _rt;
    float positionEma = 0.5f;
    float scoreEma = 0.5f;
    float matchIoU = 0.5f;
    int enterFrames = 2;
    int exitFrames = 3;
    [SerializeField, Range(0f, 1f)] float unmatchedDecay = 0.85f;

    [SerializeField, Range(0f, 2f)] float colorBlindIntensity = 1.2f;
    [SerializeField] Slider contrastSlider;
    [SerializeField] Dropdown modeDropdown;


    class TrackedBox
    {
        public Rect rect;
        public float score;
        public int classIndex;
        public int seen;     // matched frames
        public int missed;   // consecutive unmatched frames
    }

    readonly List<TrackedBox> tracks = new();
    public void SetContrastStrength(float value)
    {
        colorBlindIntensity = value;
        if (colorBlindMaterial != null)
            colorBlindMaterial.SetFloat("_ContrastStrength", colorBlindIntensity);
    }



    private void OnEnable()
    //當進入play mode， GameObject is active且元件都已經啟動時調用OnEnable()
    //OnEnable進入play mode時，總是在MonoBehaviour.Awake之後、MonoBehaviour.Start之前調用。
    {
        nn = new NNHandler(ModelFile);
        yolo = new YOLOv8Segmentation(nn);

        textureProvider = GetTextureProvider(nn.model);//68行
        textureProvider.Start();
        //WebCamTextureProvider.cs中的Start()，WebCamTextureProvider.cs 繼承textureProvider

        colorBlindMaterial = new Material(colorBlindShader);
        ImageUI.material = colorBlindMaterial;
        if (contrastSlider != null)
        {
            contrastSlider.minValue = 0f;
            contrastSlider.maxValue = 2f;
            contrastSlider.value = colorBlindIntensity;
            contrastSlider.onValueChanged.AddListener(SetContrastStrength);
        }
        if (modeDropdown != null)
        {
            modeDropdown.onValueChanged.AddListener(SetMode);
            modeDropdown.value = colorBlindMode;
        }

    }



    private void Update()
    //Update is called every frame, if the MonoBehaviour is enabled.
    {

        YOLOv8OutputReader.DiscardThreshold = MinBoxConfidence;//0.3f
        Texture2D texture = GetNextTexture();//90行，將webcam畫面調成符合模型的大小，並至中

        if (texture == null || texture.width <= 16 || texture.height <= 16)
            return;

        if (_rt == null || _rt.width != texture.width || _rt.height != texture.height)
        {
            _rt?.Release();
            _rt = new RenderTexture(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        }

        var rawBoxes = yolo.Run(texture);
        var boxes = SmoothAndTrack(rawBoxes);
        DrawResults(boxes, texture);
        ApplyColorBlindMaterial(texture);

    }

    protected TextureProvider GetTextureProvider(Model model)
    {
        var firstInput = model.inputs[0];
        int height = firstInput.shape[6];
        int width = firstInput.shape[5];
        //Debug.Log($"Input shape: [{string.Join(",", firstInput.shape)}]");
        TextureProvider provider;
        switch (textureProviderType)
        /*TextureProviderType.ProviderType型態的變數，根據一個枚舉 (enum) 變數 textureProviderType 的值
        來判斷要使用哪種 TextureProvider 具體實作（例如攝影機、影片輸入）。*/
        {
            case TextureProviderType.ProviderType.WebCam:
                provider = new WebCamTextureProvider(textureProvider as WebCamTextureProvider, width, height);
                break;
            /*textureProvider as WebCamTextureProvider 表示將已有某個 textureProvider 變數
            轉型成 WebCamTextureProvider（可能用於傳遞一些先前設定或狀態）。*/
            case TextureProviderType.ProviderType.Video:
                provider = new VideoTextureProvider(textureProvider as VideoTextureProvider, width, height);
                break;
            /*
            case TextureProviderType.ProviderType.Vuforia:
                provider = new VuforiaTextureProvider(width, height);
                break;
            */
            default:
                throw new InvalidEnumArgumentException();

        }
        return provider;
    }

    protected Texture2D GetNextTexture()
    {
        return textureProvider.GetTexture();
        /*GetTexture()在TextureProvider.cs中實作*/
    }

    void OnDisable()
    {
        nn.Dispose();
        textureProvider.Stop();

        if (_rt != null)
        {
            _rt.Release();
            _rt = null;
        }
        if (modeDropdown != null)
            modeDropdown.onValueChanged.RemoveListener(SetMode);
    }

    protected void DrawResults(IEnumerable<ResultBox> results, Texture2D img)
    {
        //對 results（一個 IEnumerable<ResultBox> 的集合）中的每一個 box 元素，都呼叫 DrawBox(box, img) 一次
        results.ForEach(box => DrawBox(box, img));

        boxCount = 0;
        foreach (var box in results)
        {
            if (boxCount >= boxBuffer.Length)
                break;

            Rect r = box.rect;             // 若 YOLO 輸出非正規化，請除以 img.width/height
            r.x /= img.width;
            r.y /= img.height;
            r.width /= img.width;
            r.height /= img.height;
            r.y = 1f - r.y - r.height;     // y 軸翻轉（RawImage UV 原點在左下）

            boxBuffer[boxCount++] = new Vector4(r.x, r.y, r.width, r.height);
        }

        //ApplyColorBlindMaterial(img); // 把整張圖（含 RGB）交給 shader
    }

    protected virtual void DrawBox(ResultBox box, Texture2D img)
    {
        Color boxColor = colorArray[box.bestClassIndex % colorArray.Length];
        int boxWidth = (int)(box.score / MinBoxConfidence);
        TextureTools.DrawRectOutline(img, box.rect, boxColor, boxWidth, rectIsNormalized: false, revertY: true);
    }

    private void OnValidate() //即時更新 textureProvider 的實例
    {
        Type t = TextureProviderType.GetProviderType(textureProviderType);
        //根據你選擇的枚舉 textureProviderType（例如「WebCam」或「Video」）取得對應的 TextureProvider 類型 Type t。
        if (textureProvider == null || t != textureProvider.GetType())
        {
            if (nn == null)
                textureProvider = RuntimeHelpers.GetUninitializedObject(t) as TextureProvider;
            else
            {
                textureProvider = GetTextureProvider(nn.model);
                textureProvider.Start();
            }

        }
    }

    void ApplyColorBlindMaterial(Texture texture)
    {

        if (colorBlindMaterial == null)
            return;

        colorBlindMaterial.SetTexture("_MainTex", texture); // 這一步把整張圖（含 RGB）交給 shader。
        colorBlindMaterial.SetVectorArray("_BoxData", boxBuffer);
        colorBlindMaterial.SetInt("_BoxCount", boxCount);
        colorBlindMaterial.SetInt("_Mode", colorBlindMode);
        //Debug.Log("SetMode: " + colorBlindMode);

        Graphics.Blit(texture, _rt, colorBlindMaterial);
        ImageUI.texture = _rt;
        //以下為更新UI角度
        colorBlindMaterial.SetFloat("_ContrastStrength", colorBlindIntensity);
        var camProvider = textureProvider as Assets.Scripts.TextureProviders.WebCamTextureProvider;
        if (camProvider != null)
        {
            float angle = -camProvider.RotationAngle; // Unity UI 方向需要反向
            ImageUI.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);

            // 如果前鏡頭鏡像，Y 軸翻轉
            if (camProvider.IsVerticallyMirrored)
                ImageUI.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            else
                ImageUI.rectTransform.localScale = Vector3.one;
        }
    }

    List<ResultBox> SmoothAndTrack(List<ResultBox> detections)
    {
        Profiler.BeginSample("Detector.SmoothAndTrack");

        // Age existing tracks
        foreach (var t in tracks) t.missed++;

        // Greedy IoU matching
        foreach (var det in detections)
        {
            TrackedBox best = null;
            float bestIoU = matchIoU;
            foreach (var t in tracks)
            {
                float iou = IntersectionOverUnion.CalculateIOU(t.rect, det.rect);
                if (iou > bestIoU)
                {
                    bestIoU = iou;
                    best = t;
                }
            }

            if (best != null)
            {
                best.rect = LerpRect(best.rect, det.rect, positionEma);
                best.score = Mathf.Lerp(best.score, det.score, scoreEma);
                best.classIndex = det.bestClassIndex;
                best.seen++;
                best.missed = 0;
            }
            else
            {
                tracks.Add(new TrackedBox
                {
                    rect = det.rect,
                    score = det.score,
                    classIndex = det.bestClassIndex,
                    seen = 1,
                    missed = 0
                });
            }
        }

        // Decay / drop old tracks
        for (int i = tracks.Count - 1; i >= 0; i--)
        {
            var t = tracks[i];
            if (t.missed > exitFrames || t.score < MinBoxConfidence * 0.3f)
            {
                tracks.RemoveAt(i);
            }
            else if (t.missed > 0)
            {
                t.score *= unmatchedDecay; // fade when temporarily lost
            }
        }

        // Only output stable tracks (hysteresis)
        var output = new List<ResultBox>();
        foreach (var t in tracks)
        {
            if (t.seen >= enterFrames && t.missed <= exitFrames && t.score >= MinBoxConfidence)
                output.Add(new ResultBox(t.rect, t.score, t.classIndex));
        }

        Profiler.EndSample();
        return output;
    }

    Rect LerpRect(Rect a, Rect b, float alpha)
    {
        return new Rect(
            Mathf.Lerp(a.x, b.x, alpha),
            Mathf.Lerp(a.y, b.y, alpha),
            Mathf.Lerp(a.width, b.width, alpha),
            Mathf.Lerp(a.height, b.height, alpha));
    }

    public void SetMode(int mode)
    {
        colorBlindMode = mode;
        if (colorBlindMaterial) colorBlindMaterial.SetInt("_Mode", mode);
        Debug.Log("SetMode: " + mode);
    }

}


