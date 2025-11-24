using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.Profiling;

namespace NN
{
    public class YOLOv8
    {
        protected YOLOv8OutputReader outputReader;

        private NNHandler nn;

        public YOLOv8(NNHandler nn)
        {
            this.nn = nn;//this為YOLOv8的物件(於Detector.cs中)
            outputReader = new();
        }

        public List<ResultBox> Run(Texture2D image)
        {
            Profiler.BeginSample("YOLO.Run");
            var outputs = ExecuteModel(image);
            var results = Postprocess(outputs);
            Profiler.EndSample();
            return results;
        }

        protected Tensor[] ExecuteModel(Texture2D image)
        {
            Tensor input = new Tensor(image);
            ExecuteBlocking(input);
            input.tensorOnDevice.Dispose();
            return PeekOutputs().ToArray();//將return轉為陣列。
        }

        private void ExecuteBlocking(Tensor preprocessed)
        {
            Profiler.BeginSample("YOLO.Execute");
            nn.worker.Execute(preprocessed);
            //Execute()及FlushSchedule()是WorkerFactory.CreateWorker()套件的API方法
            nn.worker.FlushSchedule(blocking: true);
            Profiler.EndSample();
        }

        private IEnumerable<Tensor> PeekOutputs()
        {
            foreach (string outputName in nn.model.outputs)//取出模型定義中 outputs 欄位的每個輸出名稱。
            {
                Tensor output = nn.worker.PeekOutput(outputName);//使用 worker 的 PeekOutput 方法取得對應名稱的輸出張量。
                Debug.Log($"Output shape: [{string.Join(", ", output)}]");
                yield return output;//逐一回傳 output
            }
        }

        protected List<ResultBox> Postprocess(Tensor[] outputs)//將模型輸出張量做後處理，產生物件框
        {
            Profiler.BeginSample("YOLOv8Postprocessor.Postprocess");
            Tensor boxesOutput = outputs[0];//[center_x , center_y , width , height , object]
            List<ResultBox> boxes = outputReader.ReadOutput(boxesOutput).ToList();
            boxes = DuplicatesSupressor.RemoveDuplicats(boxes);
            Profiler.EndSample();
            return boxes;
        }
    }
}