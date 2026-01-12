using Unity.Barracuda;

public class NNHandler : System.IDisposable
{
    public Model model;
    public IWorker worker;

    public NNHandler(NNModel nnmodel)
    {
        model = ModelLoader.Load(nnmodel);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharpBurst, model);//.CSharpBurst --> Unity Burst implementation, fastest CPU option
        //WorkerFactory.CreateWorker()為Barracuda的API
    }

    public void Dispose()
    {
        worker.Dispose();
    }
}
