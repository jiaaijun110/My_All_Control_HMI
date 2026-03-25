namespace Drivers.DVision.Siemens
{
    public interface ICameraDriver
    {
        bool IsOpen { get; }
        bool IsGrabbing { get; }
        event EventHandler<ImageGrabbedEventArgs>? ImageGrabbed;
        void Open();
        void StartGrabbing();
        void StopGrabbing();
        void Close();
    }
}

