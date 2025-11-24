using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.Profiling;

namespace NN
{
    public class YOLOv8OutputReader
    {
        public static float DiscardThreshold = 0.1f;
        protected const int ClassesNum = 1;
        const int BoxesPerCell = 8400;
        const int InputWidth = 640;
        const int InputHeight = 640;

        public IEnumerable<ResultBox> ReadOutput(Tensor output)
        {
            float[,] array = ReadOutputToArray(output);
            foreach (ResultBox result in ReadBoxes(array))//對 ReadBoxes(array) 所產生的每一個 ResultBox（以 result 為變數）執行下面的區塊
                yield return result;
        }


        private float[,] ReadOutputToArray(Tensor output)
        {
            var reshapedOutput = output.Reshape(new[] { 1, 1, BoxesPerCell, 5 });//改變張量 (Tensor) 的維度排列，讓後續處理變得更「線性／簡易遍歷」
            //-1 表示：讓 Barracuda 自動計算這個維度 --> 8400,5 (每個框有5個值)
            var array = TensorToArray2D(reshapedOutput);
            reshapedOutput.Dispose();
            return array;
        }

        private IEnumerable<ResultBox> ReadBoxes(float[,] array)
        {
            int boxes = array.GetLength(0); //取得第一維的大小
            for (int box_index = 0; box_index < boxes; box_index++) //對於每一個框 box_index，呼叫 ReadBox(array, box_index)
            {
                ResultBox box = ReadBox(array, box_index);
                if (box != null)
                    yield return box;//如果 ReadBox 回傳非空，就 yield return 該 ResultBox。也就是：只返回「通過門檻」的框。
            }
        }

        protected virtual ResultBox ReadBox(float[,] array, int box)
        {
            (int highestClassIndex, float highestScore) = DecodeBestBoxIndexAndScore(array, box);

            if (highestScore < DiscardThreshold)//如果最高分數小於 DiscardThreshold，則回傳 null（即丟棄該框）。
                return null;

            Rect box_rect = DecodeBoxRectangle(array, box);//計算該框在影像上的矩形 Rect

            ResultBox result = new(//建立 ResultBox 物件並回傳
                rect: box_rect,
                score: highestScore,
                bestClassIndex: highestClassIndex);
            return result;
        }

        private (int, float) DecodeBestBoxIndexAndScore(float[,] array, int box)//取得該框中最高類別的索引和分數
        {
            const int classesOffset = 4;
            int highestClassIndex = 0;
            float highestScore = 0;

            for (int i = 0; i < ClassesNum; i++)
            {
                float currentClassScore = array[box, i + classesOffset];
                if (currentClassScore > highestScore)
                {
                    highestScore = currentClassScore;
                    highestClassIndex = i;
                }
            }

            return (highestClassIndex, highestScore);
        }

        private Rect DecodeBoxRectangle(float[,] data, int box)//計算該框在影像上的矩形 Rect
        {
            const int boxCenterXIndex = 0;
            const int boxCenterYIndex = 1;
            const int boxWidthIndex = 2;
            const int boxHeightIndex = 3;

            float centerX = data[box, boxCenterXIndex];
            float centerY = data[box, boxCenterYIndex];
            float width = data[box, boxWidthIndex];
            float height = data[box, boxHeightIndex];

            float xMin = centerX - width / 2;
            float yMin = centerY - height / 2;
            xMin = xMin < 0 ? 0 : xMin;
            yMin = yMin < 0 ? 0 : yMin;
            var rect = new Rect(xMin, yMin, width, height);
            rect.xMax = rect.xMax > InputWidth ? InputWidth : rect.xMax;
            rect.yMax = rect.yMax > InputHeight ? InputHeight : rect.yMax;

            return rect;
        }

        private float[,] TensorToArray2D(Tensor tensor)
        {
            float[,] output = new float[tensor.width, tensor.channels];
            /*
            假設 BoxesPerCell = 8400，那麼 Reshape 後張量的四維為 [1, 1, 8400, paramsPerBox]（其中 paramsPerBox 是 -1 自動推算的維度大小）。
            在這樣的結構裏，你把「框數」放在第三維 (也就是 “8400”)。
            Barracuda 的 Tensor 在四維模型中其 width 屬性對應的實際是哪個維度，視 TensorShape 的用法／呼叫而定。
            你在 TensorToArray2D 中將 tensor.width 做為第一維大小，也就是把「框數」當作 width。
            */
            var data = tensor.AsFloats(); //取張量內所有浮點數值
            int expected = tensor.width * tensor.channels;
            if (data.Length != expected)
            {
                Debug.LogWarning($"TensorToArray2D mismatch: width={tensor.width}, channels={tensor.channels}, data.Length={data.Length}");
            }
            Buffer.BlockCopy(data, 0, output, 0, Buffer.ByteLength(data));


            //int bytes = Buffer.ByteLength(data); //計算整個資料的總位元組數
            //Buffer.BlockCopy(data, 0, output, 0, bytes);//將一維資料複製至你的二維陣列
            return output;
        }
    }
}