using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using HTC.UnityPlugin.Vive;
using OpenCVForUnity;
using Valve.VR;
using System;
using System.Runtime.InteropServices;

public class ComicViveFilter : MonoBehaviour
{
    public bool enableFilter = true;
    Texture2D texture = null;
    Size size;

    //tracked camera:
    CVRTrackedCamera trcam_instance = null;
    ulong pTrackedCamera = 0;
    IntPtr pBuffer = (IntPtr)null;
	byte[] buffer, staticBuffer = null;
    uint buffsize = 0;
    CameraVideoStreamFrameHeader_t pFrameHeader;
    EVRTrackedCameraError camerror = EVRTrackedCameraError.None;
    uint prevFrameSequence = 0;
	bool camError = false;

    //filter:
    Mat rgbaMat, grayMat, lineMat, maskMat, bgMat, dstMat;
    byte[] grayPixels;
    byte[] maskPixels;

	private void Start ()
	{
	    size = initTrackedCamera ();
	    initFilter (size);
	} 

	private void Update ()
	{
	    byte[] framebuffer = updateTrackedCamera ();
		if (camError)
			framebuffer = staticBuffer;
		if (framebuffer != null) {
	        if (enableFilter) {
	            updateFilter (framebuffer);
	        } else {
	            texture.LoadRawTextureData(framebuffer);
	            texture.Apply ();
	            return;
	        }
	    }
	    // toggle filter with a controller
	    if(ViveInput.GetPressDown (HandRole.RightHand, ControllerButton.Trigger)) {
	        enableFilter = !enableFilter;
	        setupTransform (enableFilter);
//			SetTronMode(!enableFilter);
	    }
	}
        
    void initFilter(Size dimension)
    {
        int w = (int)dimension.width;
        int h = (int)dimension.height;
		if (w == 0 || h == 0)
			return;
        rgbaMat = new Mat(h, w, CvType.CV_8UC4);
        grayMat = new Mat (h, w, CvType.CV_8UC2);
        lineMat = new Mat (h, w, CvType.CV_8UC2);
        maskMat = new Mat (h, w, CvType.CV_8UC2);
        dstMat = new Mat (h, w, CvType.CV_8UC2);

        texture = new Texture2D (w, h, TextureFormat.RGBA32, false);
        gameObject.GetComponent<Renderer>().material.mainTexture = texture;
        setupTransform(enableFilter);
    
        // adjust camera's orthographicSize
        float width = w, height = h;   
        float widthScale = (float)Screen.width / width;
        float heightScale = (float)Screen.height / height;
        if (widthScale < heightScale) {
            Camera.main.orthographicSize = (width * (float)Screen.height / (float)Screen.width) / 2;
        } else {
            Camera.main.orthographicSize = height / 2;
        }

        //create a striped background.
        bgMat = new Mat (h, w, CvType.CV_8UC2, new Scalar (255));
        for (int i = 0; i < bgMat.rows ()*2.5f; i=i+4) {
            Imgproc.line (bgMat, new Point (0, 0 + i), new Point (bgMat.cols (), -bgMat.cols () + i), new Scalar (0), 1);
        }
        // init arrays
        grayPixels = new byte[grayMat.cols () * grayMat.rows () * grayMat.channels ()];
        maskPixels = new byte[maskMat.cols () * maskMat.rows () * maskMat.channels ()];
    }

    private void updateFilter(byte[] framebuffer)
    {
        rgbaMat.put(0,0,framebuffer);

        Imgproc.cvtColor (rgbaMat, grayMat, Imgproc.COLOR_RGBA2GRAY);
        bgMat.copyTo (dstMat);
        Imgproc.GaussianBlur (grayMat, lineMat, new Size (3, 3), 0);
        grayMat.get (0, 0, grayPixels);

        for (int i = 0; i < grayPixels.Length; i++) {
            maskPixels [i] = 0;
            if (grayPixels [i] < 70) {
                grayPixels [i] = 0;
                maskPixels [i] = 1;
            } else if (70 <= grayPixels [i] && grayPixels [i] < 120) {
                grayPixels [i] = 100;
            } else {
                grayPixels [i] = 255;
                maskPixels [i] = 1;
            }
        }
        grayMat.put (0, 0, grayPixels);
        maskMat.put (0, 0, maskPixels);
        grayMat.copyTo (dstMat, maskMat);

        Imgproc.Canny (lineMat, lineMat, 20, 120);           
        lineMat.copyTo (maskMat);               
        Core.bitwise_not (lineMat, lineMat);         
        lineMat.copyTo (dstMat, maskMat);
        Utils.matToTexture2D (dstMat, texture);
    }

    // flip horizontal and rotate view depending if filter enabled
    public void setupTransform(bool enable) {
        int w = (int)size.width, h = (int)size.height;
		if (camError)
			enable = !enable;
        if (!enable) {
            gameObject.transform.localScale = new Vector3 (-w, h, 1);
            var rot = gameObject.transform.localRotation.eulerAngles;
            rot.Set(0f, 0f, 180f);
            gameObject.transform.localRotation = Quaternion.Euler(rot);

        } else {
            gameObject.transform.localScale = new Vector3 (w, h, 1);
            var rot = gameObject.transform.localRotation.eulerAngles;
            rot.Set(0f, 0f, 0f);
            gameObject.transform.localRotation = Quaternion.Euler(rot);
        }
    }

    private Size initTrackedCamera()
    {
        uint width = 0, height = 0, index = 0;
        bool pHasCamera = false;

        trcam_instance = OpenVR.TrackedCamera;

        if(trcam_instance == null) {
            Debug.LogError("Error getting TrackedCamera");
			camError = true;
        } else {            
            camerror = trcam_instance.HasCamera (index, ref pHasCamera);
            if(camerror != EVRTrackedCameraError.None) {
                Debug.LogError("HasCamera: EVRTrackedCameraError="+camerror);
				camError = true;
            } else if (pHasCamera) {
                camerror = trcam_instance.GetCameraFrameSize (index, EVRTrackedCameraFrameType.Undistorted, ref width, ref height, ref buffsize);
                if (camerror != EVRTrackedCameraError.None) {
                    Debug.LogError("GetCameraFrameSize: EVRTrackedCameraError=" + camerror);
					camError = true;
                } 
                else {
                    buffer = new byte[buffsize];
                    pBuffer = Marshal.AllocHGlobal ((int)buffsize);

                    camerror = trcam_instance.AcquireVideoStreamingService (index, ref pTrackedCamera);
                    if (camerror != EVRTrackedCameraError.None) {
                        Debug.LogError("AcquireVideoStreamingService: EVRTrackedCameraError=" + camerror);
						camError = true;
                    }
                }
            } else {
                Debug.Log("no camera found");
				camError = true;
            }
        }
		if(camError) {
			Texture2D tex = (gameObject.GetComponent<Renderer>().material.mainTexture as Texture2D);
			if (tex != null) {
				staticBuffer = tex.GetRawTextureData ();
			}
			Vector3 scale = gameObject.transform.localScale;
			return(new Size (scale.x, scale.y));
		} 
        return new Size (width, height);
    }

    private byte[] updateTrackedCamera()
    {
		if (camError) {
			if (texture != null) {
				return texture.GetRawTextureData ();
			} else {
				return null;
			}
		}
        // first get header only
        camerror = trcam_instance.GetVideoStreamFrameBuffer(pTrackedCamera,  EVRTrackedCameraFrameType.Undistorted, (IntPtr)null, 0, ref pFrameHeader, (uint)Marshal.SizeOf(typeof(CameraVideoStreamFrameHeader_t)));
        if(camerror != EVRTrackedCameraError.None) {
            Debug.LogError("GetVideoStreamFrameBuffer: EVRTrackedCameraError="+camerror);
            return null;
        }
        //if frame hasn't changed don't copy buffer
        if (pFrameHeader.nFrameSequence == prevFrameSequence) {
            return null;
        }
        // now get header and buffer
        camerror = trcam_instance.GetVideoStreamFrameBuffer(pTrackedCamera,  EVRTrackedCameraFrameType.Undistorted, pBuffer, buffsize, ref pFrameHeader, (uint)Marshal.SizeOf(typeof(CameraVideoStreamFrameHeader_t)));
        if(camerror != EVRTrackedCameraError.None) {
            Debug.LogError("GetVideoStreamFrameBuffer: EVRTrackedCameraError="+camerror);
            return null;
        }
        prevFrameSequence = pFrameHeader.nFrameSequence;

        //capture new frame buffer
        Marshal.Copy(pBuffer, buffer, 0, (int)buffsize);
        return buffer;
    }

    void OnDestroy ()
    {
        if (pTrackedCamera != 0) {
            trcam_instance.ReleaseVideoStreamingService(pTrackedCamera);
        }
		if (grayMat == null)
			return;
        grayMat.Dispose ();
        lineMat.Dispose ();
        maskMat.Dispose ();
        bgMat.Dispose ();
        dstMat.Dispose ();
        grayPixels = null;
        maskPixels = null;
    }

	EVRSettingsError SetTronMode(bool enable)
	{
		EVRSettingsError e = EVRSettingsError.None;
		OpenVR.Settings.SetBool(OpenVR.k_pch_Camera_Section, 
			OpenVR.k_pch_Camera_EnableCameraForCollisionBounds_Bool,
			enable, ref e);
		OpenVR.Settings.Sync(true, ref e);
		if(e==EVRSettingsError.None)
			Debug.LogError("error settingn tron mode");
		return e;
	}
}
