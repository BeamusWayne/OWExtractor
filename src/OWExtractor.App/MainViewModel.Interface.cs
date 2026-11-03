namespace OWExtractor.App;

public sealed partial class MainViewModel
{
    private bool exportOptionsSelected;
    public bool DetailsSelected { get => !exportOptionsSelected; set { if (value) SetInspector(false); } }
    public bool ExportOptionsSelected { get => exportOptionsSelected; set { if (value) SetInspector(true); } }
    private void SetInspector(bool export)
    {
        exportOptionsSelected = export;
        Notify(nameof(DetailsSelected)); Notify(nameof(ExportOptionsSelected));
    }
    public string ZoomText => $"{Camera.Zoom:P0}";
    public string QueueActionHint => LiveEnabled ? "任务将在提取队列中执行" : "离线仅保存任务，不执行提取";
    private bool queueNotice;
    public bool QueueNotice { get => queueNotice; set { queueNotice = value; Notify(); } }
    public string QueueNoticeText { get; private set; } = "";
    public async Task AddSelectedJobsAsync(IEnumerable<OWExtractor.Core.SkinAsset> selected)
    {
        await AddJobsAsync(selected);
        QueueNoticeText = Status; Notify(nameof(QueueNoticeText)); QueueNotice = true;
    }
    private void PreviewCameraChanged()
    {
        Notify(nameof(ZoomText)); Notify(nameof(FrontView)); Notify(nameof(RearView));
    }
}
