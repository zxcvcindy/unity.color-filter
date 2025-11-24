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
    protected float MinBoxConfidence = 0.3f;

    [SerializeField]
    protected TextureProviderType.ProviderType textureProviderType;//TextureProviderType.ProviderType在TextureProvider.cs

    [SerializeReference]
    protected TextureProvider textureProvider = null;

    protected NNHandler nn;
    protected Color[] colorArray = new Color[] { Color.red };

    YOLOv8 yolo;

    private void OnEnable()
    //當進入play mode， GameObject is active且元件都已經啟動時調用OnEnable()
    //OnEnable進入play mode時，總是在MonoBehaviour.Awake之後、MonoBehaviour.Start之前調用。
    {
        nn = new NNHandler(ModelFile);
        yolo = new YOLOv8Segmentation(nn);

        textureProvider = GetTextureProvider(nn.model);//68行
        textureProvider.Start();
        //WebCamTextureProvider.cs中的Start()，WebCamTextureProvider.cs 繼承textureProvider
    }

    private void Update()
    //Update is called every frame, if the MonoBehaviour is enabled.
    {
        YOLOv8OutputReader.DiscardThreshold = MinBoxConfidence;//0.3f
        Texture2D texture = GetNextTexture();//90行，將webcam畫面調成符合模型的大小，並至中

        var boxes = yolo.Run(texture);//將調整好大小的畫面匯入yolo模型，Run()函數在YOLOv8.cs中
        DrawResults(boxes, texture);
        ImageUI.texture = texture;
    }

    protected TextureProvider GetTextureProvider(Model model)
    {
        var firstInput = model.inputs[0];
        int height = firstInput.shape[6];
        int width = firstInput.shape[5];
        Debug.Log($"Input shape: [{string.Join(",", firstInput.shape)}]");
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
    }

    protected void DrawResults(IEnumerable<ResultBox> results, Texture2D img)
    {
        //對 results（一個 IEnumerable<ResultBox> 的集合）中的每一個 box 元素，都呼叫 DrawBox(box, img) 一次
        results.ForEach(box => DrawBox(box, img));
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
}
