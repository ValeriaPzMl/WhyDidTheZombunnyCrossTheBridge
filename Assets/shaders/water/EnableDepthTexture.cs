using UnityEngine;

// Hace que la camara genere _CameraDepthTexture, que el OceanShader
// usa para saber que tan profunda es el agua (refraccion y color).
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class EnableDepthTexture : MonoBehaviour
{
    void OnEnable()
    {
        GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
    }
}
