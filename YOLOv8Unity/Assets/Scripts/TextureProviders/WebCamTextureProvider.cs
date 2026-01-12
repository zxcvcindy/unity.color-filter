using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Profiling;

namespace Assets.Scripts.TextureProviders
{
    [Serializable]
    public class WebCamTextureProvider : TextureProvider
    {
        [Tooltip("Leave empty for automatic selection.")]
        [SerializeField]
        private string cameraName;
        private WebCamTexture webCamTexture;
        public int RotationAngle => webCamTexture != null ? webCamTexture.videoRotationAngle : 0;
        public bool IsVerticallyMirrored => webCamTexture != null && webCamTexture.videoVerticallyMirrored;

        public WebCamTextureProvider(int width, int height, TextureFormat format = TextureFormat.RGB24, string cameraName = null) : base(width, height, format)
        /*「: base(width, height, format)」這表示該構造函式呼叫其父類別（TextureProvider）的構造函式，
        並把 width, height, format 傳給父類別。也就是：父類別 TextureProvider 在其構造中會用這三個參數來建立其 
        ResultTexture = new Texture2D(width, height, format, mipChain:false)。
        所以，當你建立一個 WebCamTextureProvider 時，會先呼叫 TextureProvider 的構造，把貼圖尺寸＆格式設定好。*/
        {
            cameraName = cameraName != null ? cameraName : SelectCameraDevice();
            webCamTexture = new WebCamTexture(cameraName); //WebCamTexture()為Unity API，這樣可以設定從指定的攝影機裝置讀取影像輸出為 Texture。
            InputTexture = webCamTexture;
        }

        public WebCamTextureProvider(WebCamTextureProvider provider, int width, int height, TextureFormat format = TextureFormat.RGB24) : this(width, height, format, provider?.cameraName)
        {
        }

        public override void Start()
        {
            webCamTexture.Play();
            /*
            Play()是 Unity Engine 裡的 WebCamTexture 類別的一個成員方法，
            WebCamTexture 是 Unity Engine 提供的類別，用來處理「設備攝像頭」輸入，並將其作為一個可用於貼圖／材質的實時 Texture。
            當你的程式呼叫 webCamTexture.Play(); 時，是在說「開始從攝像頭抓影像，並將影像輸出到 webCamTexture 這個物件所代表的 Texture 上」。
            */



        }

        public override void Stop()
        {
            if (webCamTexture == null)
                return;

            if (webCamTexture.isPlaying)
                webCamTexture.Stop();
        }

        public override TextureProviderType.ProviderType TypeEnum()
        {
            return TextureProviderType.ProviderType.WebCam;
        }

        /// <summary>
        /// Return first backfaced camera name if avaible, otherwise first possible
        /// </summary>
        private string SelectCameraDevice()
        {
            if (WebCamTexture.devices.Length == 0)
                throw new Exception("Any camera isn't avaible!");

            foreach (var cam in WebCamTexture.devices)
            {
                if (!cam.isFrontFacing)
                    return cam.name;
            }
            return WebCamTexture.devices[0].name;
        }

    }
}