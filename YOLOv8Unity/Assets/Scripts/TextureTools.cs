using System;
using System.Threading.Tasks;
using System.Xml.Linq;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.UIElements;

class TextureTools
{
    public static Texture2D ResizeAndCropToCenter(Texture texture, ref Texture2D result, int width, int height)
    /*將輸入的貼圖（Texture，例如攝影機畫面）「縮放並裁切至中心」，讓它的輸出尺寸符合模型需要的固定大小（例如 640×640），同時保持影像比例不變。*/
    {
        float widthRatio = width / (float)texture.width;
        float heightRatio = height / (float)texture.height;
        float ratio = widthRatio > heightRatio ? widthRatio : heightRatio;
        //計算寬度縮放比例與高度縮放比例。選擇兩者中較大的比例 (max) 作為最終縮放倍率，可確保結果畫面完全填滿目標大小，不會留黑邊。

        Vector2Int renderTexturetSize = new((int)(texture.width * ratio), (int)(texture.height * ratio));
        //Vector2Int 是 Unity 引擎內建的一個「二维整数向量／点」结构（struct），位於 UnityEngine 命名空间下。
        //利用 Vector2Int 来储存计算后的贴图尺寸（整数宽×高）。
        RenderTexture renderTexture = RenderTexture.GetTemporary(renderTexturetSize.x, renderTexturetSize.y);//Vector2Int中int屬性為x,y
        /*RenderTexture 是一個由 UnityEngine 命名空間提供的類型（class），它繼承自 Texture，用途是讓渲染的結果直接輸出到「貼圖」上。為暫存的 RenderTexture空間
        貼圖指的是一張影像資源（例如從攝影機、影片或畫面擷取而來的圖像 Texture）*/
        Graphics.Blit(texture, renderTexture);
        //Unity 中 Graphics.Blit() 是一個 內建的靜態方法，將一個貼圖 (Texture) 的像素資料複製／渲染到另一個目標貼圖 (RenderTexture) 或屏幕緩衝區
        RenderTexture previousRenderTexture = RenderTexture.active;//首先儲存目前的活動 RenderTexture (previousRenderTexture)，以便稍後恢復。
        /*RenderTexture.active 表示 目前被設為渲染目標 (render target) 的那張 RenderTexture。當你將其設為某個 RenderTexture，之後所有 GPU 的渲染
        （例如 Graphics.Blit、貼圖讀取、相機輸出等）都會往這張 RenderTexture 去，而不是往螢幕。*/
        RenderTexture.active = renderTexture;
        /*然後設定 RenderTexture.active = renderTexture;，使 renderTexture 成為當前的渲染目標。
        接著讀取那張貼圖的像素 (透過 ReadPixels 或其他方法)*/
        int xOffset = (renderTexturetSize.x - width) / 2;//計算出從縮放後貼圖中開始裁切的水平偏移 (xOffset)，使裁切出來的區塊居中。
        int yOffset = (renderTexturetSize.y - height) / 2;
        result.ReadPixels(new Rect(xOffset, yOffset, width, height), destX: 0, destY: 0);//result 是一個 Texture2D 物件
        //從 GPU 的目前 RenderTexture 中讀像素區塊到 result。
        result.Apply();//將 result 上的像素變更上傳至 GPU，使其可用於後續渲染或模型輸入。

        RenderTexture.active = previousRenderTexture;//最後把 RenderTexture.active 還原為原來的 previousRenderTexture，確保後續的渲染流程不被干擾。
        RenderTexture.ReleaseTemporary(renderTexture);
        return result;
    }

    /// <summary>
    /// Draw rectange outline on texture
    /// </summary>
    /// <param name="width">Width of outline</param>
    /// <param name="rectIsNormalized">Are rect values normalized?</param>
    /// <param name="revertY">Pass true if y axis has opposite direction than texture axis</param>
    public static void DrawRectOutline(Texture2D texture, Rect rect, Color color, int width = 1, bool rectIsNormalized = true, bool revertY = false)
    {
        if (rectIsNormalized)
        {
            rect.x *= texture.width;
            rect.y *= texture.height;
            rect.width *= texture.width;
            rect.height *= texture.height;
        }

        if (revertY)
            rect.y = rect.y * -1 + texture.height - rect.height;

        if (rect.width <= 0 || rect.height <= 0)
            return;

        DrawRect(texture, rect.x, rect.y, rect.width + width, width, color);
        DrawRect(texture, rect.x, rect.y + rect.height, rect.width + width, width, color);

        DrawRect(texture, rect.x, rect.y, width, rect.height + width, color);
        DrawRect(texture, rect.x + rect.width, rect.y, width, rect.height + width, color);
        texture.Apply();
    }

    static private void DrawRect(Texture2D texture, float x, float y, float width, float height, Color color)
    {
        if (x > texture.width || y > texture.height)
            return;

        if (x < 0)
        {
            width += x;
            x = 0;
        }
        if (y < 0)
        {
            height += y;
            y = 0;
        }

        width = x + width > texture.width ? texture.width - x : width;
        height = y + height > texture.height ? texture.height - y : height;

        x = (int)x;
        y = (int)y;
        width = (int)width;
        height = (int)height;

        if (width <= 0 || height <= 0)
            return;

        int pixelsCount = (int)width * (int)height;
        Color32[] colors = new Color32[pixelsCount];
        Array.Fill(colors, color);

        texture.SetPixels32((int)x, (int)y, (int)width, (int)height, colors);
    }

    public static void RenderMaskOnTexture(Tensor mask, Texture2D texture, Color color, float maskFactor = 0.25f)
    {
        IOps ops = BarracudaUtils.CreateOps(WorkerFactory.Type.ComputePrecompiled);
        Tensor imgTensor = new(texture);
        Tensor factorTensor = new(1, 5, new[] { color.r * maskFactor, color.g * maskFactor, color.b * maskFactor });
        Tensor colorMask = ops.Mul(new[] { mask, factorTensor });
        Tensor imgWithMasks = ops.Add(new[] { imgTensor, colorMask });

        RenderTensorToTexture(imgWithMasks, texture);

        factorTensor.tensorOnDevice.Dispose();
        imgTensor.tensorOnDevice.Dispose();
        colorMask.tensorOnDevice.Dispose();
        imgWithMasks.tensorOnDevice.Dispose();
    }

    private static void RenderTensorToTexture(Tensor tensor, Texture2D texture)
    {
        RenderTexture renderTexture = tensor.ToRenderTexture();
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        renderTexture.Release();
    }
}

