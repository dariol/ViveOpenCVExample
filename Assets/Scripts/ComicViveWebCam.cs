using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using OpenCVForUnity;

public class ComicViveWebCam : MonoBehaviour
{
    Mat grayMat;
    Mat lineMat;
    Mat maskMat;
    Mat bgMat;
    Mat dstMat;
    byte[] grayPixels;
    byte[] maskPixels;

    Texture2D texture;

	bool initialized = false;

	WebCamTextureToMHelper webCamTextureToMatHelper;

    void Start ()
    {
		WebCamDevice[] devices = WebCamTexture.devices;
		for(int i = 0; i < devices.Length; i++)
			print("Webcam "+i+" =" + devices[i].name);

		webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMHelper> ();
		webCamTextureToMatHelper.Initialize();
    }

    public void OnWebCamTextureToMatHelperInitialized ()
    {
		Debug.Log ("OnWebCamTextureToMatHelperInitialized");
    
        Mat webCamTextureMat = webCamTextureToMatHelper.GetMat ();
    
        texture = new Texture2D (webCamTextureMat.cols (), webCamTextureMat.rows (), TextureFormat.RGBA32, false);
		Debug.Log ("rows " + webCamTextureMat.rows () + " cols " + webCamTextureMat.cols ());

        gameObject.GetComponent<Renderer> ().material.mainTexture = texture;
    
        gameObject.transform.localScale = new Vector3 (webCamTextureMat.cols (), webCamTextureMat.rows (), 1);
    
        Debug.Log ("Screen.width " + Screen.width + " Screen.height " + Screen.height + " Screen.orientation " + Screen.orientation);
    
        float width = webCamTextureMat.width();
        float height = webCamTextureMat.height();
    
        float widthScale = (float)Screen.width / width;
        float heightScale = (float)Screen.height / height;
        if (widthScale < heightScale) {
            Camera.main.orthographicSize = (width * (float)Screen.height / (float)Screen.width) / 2;
        } else {
            Camera.main.orthographicSize = height / 2;
        }

        grayMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC2);
        lineMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC2);
        maskMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC2);
        
        //create a striped background.
        bgMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC2, new Scalar (255));
        for (int i = 0; i < bgMat.rows ()*2.5f; i=i+4) {
            Imgproc.line (bgMat, new Point (0, 0 + i), new Point (bgMat.cols (), -bgMat.cols () + i), new Scalar (0), 1);
        }
        
        dstMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC2);

        grayPixels = new byte[grayMat.cols () * grayMat.rows () * grayMat.channels ()];
        maskPixels = new byte[maskMat.cols () * maskMat.rows () * maskMat.channels ()];
		initialized = true;
    }

    void Update ()
    {
        if (webCamTextureToMatHelper.IsPlaying () && webCamTextureToMatHelper.DidUpdateThisFrame () && initialized) {
			
            Mat rgbaMat = webCamTextureToMatHelper.GetMat ();
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
			Utils.matToTexture2D (dstMat, texture);//, webCamTextureToMatHelper.GetBufferColors());
      }
    }
		
    void OnDestroy ()
    {
        webCamTextureToMatHelper.Dispose ();
    }

	public void OnWebCamTextureToMatHelperDisposed ()
	{
		Debug.Log ("OnWebCamTextureToMatHelperDisposed");
		grayMat.Dispose ();
		lineMat.Dispose ();
		maskMat.Dispose ();
		bgMat.Dispose ();
		dstMat.Dispose ();
		grayPixels = null;
		maskPixels = null;
	}

	public void OnWebCamTextureToMatHelperErrorOccurred(WebCamTextureToMHelper.ErrorCode errorCode){
		Debug.Log ("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);
	}

    public void OnBackButtonClick ()
    {
        #if UNITY_5_3 || UNITY_5_3_OR_NEWER
        SceneManager.LoadScene ("OpenCVForUnityExample");
        #else
        Application.LoadLevel ("OpenCVForUnityExample");
        #endif
    }
    public void OnPlayButtonClick ()
    {
        webCamTextureToMatHelper.Play ();
    }
    public void OnPauseButtonClick ()
    {
        webCamTextureToMatHelper.Pause ();
    }
    public void OnStopButtonClick ()
    {
        webCamTextureToMatHelper.Stop ();
    }
    public void OnChangeCameraButtonClick ()
    {
		webCamTextureToMatHelper.Initialize ("HTC Vive", 612, 460, false, 60);
    }
}