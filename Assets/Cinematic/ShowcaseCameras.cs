using System.IO;
using UnityEngine;

public class ShowcaseCameras : MonoBehaviour
{
    public Camera[] cameras;
    public int current;
    public string captureFolder = "Capturas";
    public int captureScale = 2;

    void Start()
    {
        Show(current);
    }

    void Update()
    {
        if (cameras == null || cameras.Length == 0) return;

        for (int i = 0; i < cameras.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Show(i);

        if (Input.GetKeyDown(KeyCode.RightArrow)) Show((current + 1) % cameras.Length);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Show((current + cameras.Length - 1) % cameras.Length);

        if (Input.GetKeyDown(KeyCode.P)) Capture();
    }

    void Show(int index)
    {
        if (cameras == null || cameras.Length == 0) return;
        current = Mathf.Clamp(index, 0, cameras.Length - 1);
        for (int i = 0; i < cameras.Length; i++)
            if (cameras[i] != null) cameras[i].gameObject.SetActive(i == current);
    }

    void Capture()
    {
        Directory.CreateDirectory(captureFolder);
        string name = cameras[current] != null ? cameras[current].name : "Captura";
        string file = Path.Combine(captureFolder, name + "_" + System.DateTime.Now.ToString("HHmmss") + ".png");
        ScreenCapture.CaptureScreenshot(file, captureScale);
        Debug.Log("Captura guardada en " + Path.GetFullPath(file));
    }
}
