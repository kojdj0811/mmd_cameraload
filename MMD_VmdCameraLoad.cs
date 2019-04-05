using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

public class MMD_VmdCameraLoad : MonoBehaviour
{

    public bool isStartAuto = true;

    public TextAsset Select_VMD;     //vmdファイル
    public GameObject CameraCenter;  //カメラ中心
    public Camera MainCamera;        //カメラ
    public GameObject MMD_model;     //MMDモデル

    public List<CameraTransData> cameraTransDatas;
    public List<Vector2> cutTime_MinMax;

    private Vector3 initCameraCenterPos;
    private Vector3 initCameraCenterRot;


    private bool success = false;    //データの準備完了フラグ
    CameraData[] Cam_m;
    private int t = 0;
    private float t_f = 0.0f;
    private Animator target_animator;
    private float nowframe = 0.0f;

    const int HEADER = 50;            //ヘッダーの桁数
    const int MOTIONCOUNT = 4;        //モーションレコード数
    const int MOTIONDATA = 111;       //モーションデータ（レコード数分）
    const int SKINCOUNT = 4;          //スキンレコード数
    const int SKINDATA = 23;          //スキンデータ（レコード数分）
    const int CAMERACOUNT = 4;        //カメラレコード数
    const int CAMERADATA = 61;        //カメラデータ（レコード数分）
    const int ILLUMINATIONCOUNT = 4;  //照明レコード数
    const int ILLUMINATIONDATA = 28;  //照明データ（レコード数分）
    const int SHADOWCOUNT = 4;        //シャドウレコード数
    const int SHADOWDATA = 9;         //シャドウデータ（レコード数分）  

    [StructLayout(LayoutKind.Explicit)]
    public struct Union
    {
        [FieldOffset(0)]
        public float float0;

        [FieldOffset(0)]
        public int int0;
    }

    //カメラ情報
    public struct CameraData
    {
        public int frame;       //対象フレーム
        public float distans;   //距離

        public float Pos_x;     //カメラ中心のx座標
        public float Pos_y;     //カメラ中心のy座標
        public float Pos_z;     //カメラ中心のz座標

        public float Rot_x;     //カメラ中心のx軸回転
        public float Rot_y;     //カメラ中心のy軸回転
        public float Rot_z;     //カメラ中心のz軸回転

        public float viewAngle; //視野角

        public int[] Bezier;    //ベジェ曲線の補間パラメータ
        public bool originalframe;
    }

    //This is my custom class to use easily.
    public class CameraTransData
    {
        public Vector3 localPosition;
        public float distance;
        public Quaternion rotation;
    }

    void Awake()
    {
        if (!isStartAuto) return;

        initCameraCenterPos = CameraCenter.transform.position;
        initCameraCenterRot = CameraCenter.transform.rotation.eulerAngles;

        target_animator = MMD_model.GetComponent<Animator>();
    }
    // Use this for initialization
    void Start()
    {

        byte[] raw_data_org = Select_VMD.bytes;
        byte[] frameSum = new byte[4];
        int frameSum_int = 0;
        byte[] frame_data = new byte[4];
        byte[] frame_data_1byte = new byte[1];

        int index = HEADER + MOTIONCOUNT + SKINCOUNT; //カメラレコード数を格納している位置まで飛ばす

        //レコード数の取得
        frameSum[0] = raw_data_org[index++];
        frameSum[1] = raw_data_org[index++];
        frameSum[2] = raw_data_org[index++];
        frameSum[3] = raw_data_org[index++];
        frameSum_int = System.BitConverter.ToInt32(frameSum, 0);
        CameraData[] Cam = new CameraData[frameSum_int]; //レコード数分要素を用意

        // データ取得
        for (int i = 0; i < frameSum_int; i++)
        {
            //フレーム
            frame_data[0] = raw_data_org[index++];
            frame_data[1] = raw_data_org[index++];
            frame_data[2] = raw_data_org[index++];
            frame_data[3] = raw_data_org[index++];
            Cam[i].frame = System.BitConverter.ToInt32(frame_data, 0);
            //距離
            Cam[i].distans = getVmdCamera(ref index, raw_data_org);
            //位置
            Cam[i].Pos_x = getVmdCamera(ref index, raw_data_org);
            Cam[i].Pos_y = getVmdCamera(ref index, raw_data_org);
            Cam[i].Pos_z = getVmdCamera(ref index, raw_data_org);
            Cam[i].Rot_x = getVmdCamera(ref index, raw_data_org);
            //角度(ラジアンから変換)
            conversionAngle(ref Cam[i].Rot_x);
            Cam[i].Rot_y = getVmdCamera(ref index, raw_data_org);
            conversionAngle(ref Cam[i].Rot_y);
            Cam[i].Rot_z = getVmdCamera(ref index, raw_data_org);
            conversionAngle(ref Cam[i].Rot_z);
            //ベジェ曲線
            Cam[i].Bezier = new int[24];
            for (int j = 0; j < 24; j++)
            {
                frame_data_1byte[0] = raw_data_org[index++];
                Cam[i].Bezier[j] = System.Convert.ToInt32(System.BitConverter.ToString(frame_data_1byte, 0), 16);
            }
            //視野角
            frame_data[0] = raw_data_org[index++];
            frame_data[1] = raw_data_org[index++];
            frame_data[2] = raw_data_org[index++];
            frame_data[3] = raw_data_org[index++];
            Cam[i].viewAngle = System.BitConverter.ToInt32(frame_data, 0);
            index += 1;  //パース分１バイト飛ばす
        }

        //並び順バラバラなのでソート
        Qsort(ref Cam, 0, Cam.Length - 1);
        //以降補間処理
        Cam_m = new CameraData[Cam[frameSum_int - 1].frame + 1]; //最終フレーム分用意

        //３次ベジェ曲線補間
        Cam_m[0] = Cam[0];//１レコード目をコピー
        Cam_m[0].originalframe = true;
        int Addframe = 0;
        int wIndex = 1;

        for (int i = 0; i < frameSum_int - 1; i++)
        {
            Addframe = Cam[i + 1].frame - Cam[i].frame;
            for (int j = 1; j < Addframe; j++)
            {

                Cam_m[wIndex].frame = wIndex;
                Cam_m[wIndex].Pos_x = Cam[i].Pos_x + (Cam[i + 1].Pos_x - Cam[i].Pos_x) *
                                      (BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[0], Cam[i + 1].Bezier[2]),
                                                   new Vector2(Cam[i + 1].Bezier[1], Cam[i + 1].Bezier[3]), new Vector2(127, 127),
                                                   (float)(1.0 * j / (Addframe))).y) / 127;
                Cam_m[wIndex].Pos_y = Cam[i].Pos_y + (Cam[i + 1].Pos_y - Cam[i].Pos_y) *
                                      (BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[4], Cam[i + 1].Bezier[6]),
                                                   new Vector2(Cam[i + 1].Bezier[5], Cam[i + 1].Bezier[7]), new Vector2(127, 127),
                                                   (float)(1.0 * j / (Addframe))).y) / 127;
                Cam_m[wIndex].Pos_z = Cam[i].Pos_z + (Cam[i + 1].Pos_z - Cam[i].Pos_z) *
                                      (BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[8], Cam[i + 1].Bezier[10]),
                                                   new Vector2(Cam[i + 1].Bezier[9], Cam[i + 1].Bezier[11]), new Vector2(127, 127),
                                                   (float)(1.0 * j / (Addframe))).y) / 127;
                Cam_m[wIndex].Rot_x = Cam[i].Rot_x + (Cam[i + 1].Rot_x - Cam[i].Rot_x) *
                                      (BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[12], Cam[i + 1].Bezier[14]),
                                                   new Vector2(Cam[i + 1].Bezier[13], Cam[i + 1].Bezier[15]), new Vector2(127, 127),
                                                   (float)(1.0 * j / (Addframe))).y) / 127;
                Cam_m[wIndex].Rot_y = Cam[i].Rot_y + (Cam[i + 1].Rot_y - Cam[i].Rot_y) *
                                      (BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[12], Cam[i + 1].Bezier[14]),
                                                   new Vector2(Cam[i + 1].Bezier[13], Cam[i + 1].Bezier[15]), new Vector2(127, 127),
                                                   (float)(1.0 * j / (Addframe))).y) / 127;
                Cam_m[wIndex].Rot_z = Cam[i].Rot_z + (Cam[i + 1].Rot_z - Cam[i].Rot_z) *
                                      (BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[12], Cam[i + 1].Bezier[14]),
                                                   new Vector2(Cam[i + 1].Bezier[13], Cam[i + 1].Bezier[15]), new Vector2(127, 127),
                                                   (float)(1.0 * j / (Addframe))).y) / 127;
                Cam_m[wIndex].distans = Cam[i].distans + (Cam[i + 1].distans - Cam[i].distans) *
                                      (BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[16], Cam[i + 1].Bezier[18]),
                                                   new Vector2(Cam[i + 1].Bezier[17], Cam[i + 1].Bezier[19]), new Vector2(127, 127),
                                                   (float)(1.0 * j / (Addframe))).y) / 127;
                Cam_m[wIndex].viewAngle = Cam[i].viewAngle + (Cam[i + 1].viewAngle - Cam[i].viewAngle) *
                                      (int)(BezierCurve(new Vector2(0, 0), new Vector2(Cam[i + 1].Bezier[20], Cam[i + 1].Bezier[22]),
                                                        new Vector2(Cam[i + 1].Bezier[21], Cam[i + 1].Bezier[23]), new Vector2(127, 127),
                                                        (float)(1.0 * j / (Addframe))).y) / 127;
                wIndex++;
            }
            Cam_m[wIndex] = Cam[i + 1];
            Cam_m[wIndex++].originalframe = true;
        }


        //Cast camera data to my custom class
        cameraTransDatas = new List<CameraTransData>();
        for (int i = 0; i < Cam_m.Length; i++)
        {
            CameraTransData data = new CameraTransData();
            data.localPosition = new Vector3(Cam_m[i].Pos_x, Cam_m[i].Pos_y, Cam_m[i].Pos_z);
            data.distance = Cam_m[i].distans;
            data.rotation = new Quaternion();
            data.rotation = Quaternion.Euler(Cam_m[i].Rot_x, Cam_m[i].Rot_y, Cam_m[i].Rot_z);

            cameraTransDatas.Add(data);
        }


        success = true;
    }

    private void Update()
    {
        MoveCamera(Time.timeSinceLevelLoad);
        //nowframe = getAnimationFrame();
    }

    public void MoveCamera(float nowFrame)
    {
        //カメラ情報が完成したらカメラスタート
        if (success)
        {
            float returnValue = 0.0f;
            var clipInfoList = target_animator.GetCurrentAnimatorClipInfo(0);
            if (clipInfoList != null)
                returnValue = target_animator.runtimeAnimatorController.animationClips[0].length;

            float currentAnimationTime = (nowFrame / returnValue) * cameraTransDatas.Count;
            int currentCameraIndex = (int)currentAnimationTime;
            float u = currentAnimationTime - currentCameraIndex;

            foreach (Vector2 cutTime in cutTime_MinMax)
                if (nowFrame > cutTime.x && nowFrame < cutTime.y)
                    return;



            if (currentCameraIndex > cameraTransDatas.Count)
                return;


            Vector3 targetCameraCenterPos = cameraTransDatas[currentCameraIndex].localPosition / 12.5f;
            Vector3 nextCameraCenterPos = cameraTransDatas[currentCameraIndex + 1].localPosition / 12.5f;
            targetCameraCenterPos = Vector3.Lerp(targetCameraCenterPos, nextCameraCenterPos, u);

            Vector3 targetMainCameraPos = new Vector3(transform.localPosition.x, transform.localPosition.y, cameraTransDatas[currentCameraIndex].distance / 12.5f);
            Vector3 nextMainCameraPos = new Vector3(transform.localPosition.x, transform.localPosition.y, cameraTransDatas[currentCameraIndex + 1].distance / 12.5f);
            targetMainCameraPos = Vector3.Lerp(targetMainCameraPos, nextMainCameraPos, u);

            Quaternion targetCameraCenterRot = cameraTransDatas[currentCameraIndex].rotation;
            Quaternion nextCameraCenterRot = cameraTransDatas[currentCameraIndex + 1].rotation;
            targetCameraCenterRot = Quaternion.Lerp(targetCameraCenterRot, nextCameraCenterRot, u);


            CameraCenter.transform.localPosition = targetCameraCenterPos + initCameraCenterPos;
            transform.localPosition = targetMainCameraPos;
            CameraCenter.transform.rotation = Quaternion.Euler(targetCameraCenterRot.eulerAngles + initCameraCenterRot);
        }
    }

    private float getAnimationFrame()
    {

        float returnValue = 0.0f;

        var clipInfoList = target_animator.GetCurrentAnimatorClipInfo(0);
        if (clipInfoList != null)
            returnValue = clipInfoList.Length;

        return returnValue;
    }

    float getVmdCamera(ref int index, byte[] data)
    {
        Union union = new Union();
        byte[] raw_data = new byte[4];

        raw_data[0] = data[index++];
        raw_data[1] = data[index++];
        raw_data[2] = data[index++];
        raw_data[3] = data[index++];
        union.int0 = System.BitConverter.ToInt32(raw_data, 0);

        return (union.float0);
    }
    // ラジアンから角度を取得
    void conversionAngle(ref float rot)
    {
        rot = (float)(rot * 180 / System.Math.PI);
    }
    //クイックソート
    void Qsort(ref CameraData[] data, int left, int right)
    {
        int i, j;
        int pivot;
        CameraData tmp;

        i = left; j = right;
        pivot = data[(left + right) / 2].frame;
        do
        {
            while ((i < right) && (data[i].frame < pivot)) i++;
            while ((j > left) && (pivot < data[j].frame)) j--;
            if (i <= j)
            {
                tmp = data[i];
                data[i] = data[j];
                data[j] = tmp;
                i++; j--;
            }
        } while (i <= j);
        if (left < j) Qsort(ref data, left, j);
        if (i < right) Qsort(ref data, i, right);
    }

    //以降ベジェ曲線に関する関数
    float BezierCurveX(float x1, float x2, float x3, float x4, float t)
    {
        return Mathf.Pow(1 - t, 3) * x1 + 3 * Mathf.Pow(1 - t, 2) * t * x2 + 3 * (1 - t) * Mathf.Pow(t, 2) * x3 + Mathf.Pow(t, 3) * x4;
    }
    float BezierCurveY(float y1, float y2, float y3, float y4, float t)
    {
        return Mathf.Pow(1 - t, 3) * y1 + 3 * Mathf.Pow(1 - t, 2) * t * y2 + 3 * (1 - t) * Mathf.Pow(t, 2) * y3 + Mathf.Pow(t, 3) * y4;
    }
    Vector2 BezierCurve(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float t)
    {
        return new Vector2(
            BezierCurveX(p1.x, p2.x, p3.x, p4.x, t),
            BezierCurveY(p1.y, p2.y, p3.y, p4.y, t));
    }

}
