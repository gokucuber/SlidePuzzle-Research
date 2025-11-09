using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class Controller : MonoBehaviour
{
    #region public変数
    [HideInInspector] public int clickedNum = 0;//クリックされた数字
    [HideInInspector] public int tileNum;　//タイルの数
    [HideInInspector] public int rowNum;
    [HideInInspector] public int finCount = 0;
    public int clickCount = 0; //クリックした回数
    public bool isCountInterpretation;
    public float tileInterval; //タイル間隔
    public float thickness = 0.5f; //タイルの暑さ
    public float finishTime = 0.0f; //完成した時間
    public IDA ida = null;
    [HideInInspector] public bool isStart = false; //スタートしているか
    [HideInInspector] public bool isfinish = false; //フィニッシュしているか(なんでstaticしてたんだろ...)
    [HideInInspector] public bool isStop = false; //スタートしているか
    [HideInInspector] public List<Vector3> numberTransform = new List<Vector3>(); //左上から123...としたときのそれぞれの座標（不変　右下0）
    public Button shuffle; 
    public Button retry;
    public Button push;
    public GameObject[] numberArray; //タイル（012...の順番）
    public TextMeshPro TimeText; //時間表示
    public TextMeshPro MoveCountText; //動かした数表示
    public TextMeshPro FinishText; //完成した文字表示
    [HideInInspector] public List<int> nowState = new List<int>(); //現在の並び
    [HideInInspector]public List<int> finishState = new List<int>(); //完成状態
    [HideInInspector] public List<int> startState = new List<int>(); //開始状態
    [HideInInspector] public List<int> nextState = new List<int>();

    #endregion

    #region private変数

    private constCtr cCtr;

    private List<int> numbers = new List<int>();　//ランダムに数字が並ぶリスト（重複しない）
    private List<int> parityCheck = new List<int>(); //シャッフル後の配置（パリティ確認用）
    

    private float startTime = 0.0f; //開始時間
    

    
    private int tentoCount = 0; //転倒数（パリティチェック用）    
    private int color = 0;
    
    #endregion

    void Start()
    {
        cCtr = GetComponent<constCtr>();
        clickDetect[] tiles = GetComponentsInChildren<clickDetect>(true);
        //リスト初期化

        rowNum = (int)Mathf.Sqrt(tileNum + 1);

        for (int i = 0; i <= tileNum; i++)
        {
            Vector3 temp = new Vector3(i, i, i);
            numberTransform.Add(temp);
            numbers.Add(i);
            parityCheck.Add(i);
            nowState.Add(i);
            finishState.Add(i);
            startState.Add(i);
            nextState.Add(i);
        }
        finishState.RemoveAt(0);
        finishState.Add(0);

        //numberTransformの中身設定24にするなら増やす（constCtrのなかも）
        if (tileNum == 8)
        {
            cCtr.setTransArray8();
        }
        else if (tileNum == 15)
        {
            cCtr.setTransArray15();
        }

        //乱数のシード値を設定
        UnityEngine.Random.InitState((int)DateTime.Now.Ticks);

        //リトライボタン設定
        shuffle.onClick.AddListener(OnButtonClick1);
        retry.onClick.AddListener(OnButtonClick2);
        push.onClick.AddListener(OnButtonClick3);

        // ゲーム開始時のシャッフル
        Shuffle();

        for (int i = 0; i <= tileNum; i++)//開始状態設定
        {
            startState[i] = nowState[i];
        }


    }

    void Update()
    {
        //Debug.Log(isCountInterpretation);
        MoveCountText.text = "Move " + clickCount;
        if ((!isStart) && (clickCount > 0)&&(!isfinish))
        {
            finCount = 0;
            isStart = true;
            isfinish = false;
            startTime = Time.time;
        }
        if ((nowState.SequenceEqual(finishState)&&finCount==0)||finCount==2)
        {
            finishTime = Time.time;
            isfinish = true;
            isStart = false;
            finCount = 1;
        }
        if (isStart)
        {
            FinishText.text = "";
            TimeText.text = "Time " + ((Time.time - startTime).ToString("N2"));
            
        }
        if (isfinish)
        { 
            FinishText.text = "Finish!";
            TimeText.text = "Time " + ((finishTime-startTime).ToString("N2"));
        }
    }

    //ボタン押された際の処理
    void OnButtonClick1()
    {
        isfinish = false;
        isStart = false;
        isStop = true;
        //ida.isSearchStart = false;
        clickCount = 0;
        TimeText.text = "Time 00.00";
        MoveCountText.text = "Move 0";
        FinishText.text = "";
        Shuffle();
        for (int i = 0; i <= tileNum; i++)//開始状態設定
        {
            startState[i] = nowState[i];
        }
    }
    void OnButtonClick2()
    {
        isfinish = false;
        isStart = false;
        isStop = true;
        //ida.isSearchStart = false;
        clickCount = 0;
        TimeText.text = "Time 00.00";
        MoveCountText.text = "Move 0";
        FinishText.text = "";
        
        for (int i = 0; i <= tileNum; i++)//開始状態設定
        {
            nowState[i] = startState[i];
        }
        stateTile();
    }

    //pushのオンオフ
    void OnButtonClick3()
    {
        isCountInterpretation = !isCountInterpretation;
    }

    //位置交換
    void Swap(float transformCheck, float empty, GameObject Clicked, bool isX, int adjNum)
    {
        for (int k = adjNum; k >= 1; k--)
        {
            int empLoc = 0; //NowStateの中にある0の位置
            int clickedNumLoc = 0; //クリックされた数字がNowStateの中にある位置

            //交換する対象選択

            for (int j = 0; j <= tileNum; j++)
            {
                if ((k > 1) && (nowState[j] == clickedNum))//(k == adjNum) &&を消したが...adjNumをkにしたが...
                {
                    if (isX)
                    {
                        if (transformCheck < empty)
                        {
                            clickedNumLoc = j + (k - 1);
                        }
                        else
                        {
                            clickedNumLoc = j - (k - 1);
                        }
                    }
                    else
                    {
                        if (transformCheck > empty)
                        {
                            clickedNumLoc = j + (k - 1) * (int)Math.Sqrt((tileNum + 1));
                        }
                        else
                        {
                            clickedNumLoc = j - (k - 1) * (int)Math.Sqrt((tileNum + 1));
                        }
                    }
                }
                else if ((k == 1) && (nowState[j] == clickedNum))
                {
                    clickedNumLoc = j;
                }

            }
            for (int i = 0; i <= tileNum; i++)
            {
                if (nowState[i] == 0)
                {
                    empLoc = i;
                }
            }

            //nowStateの中の交換
            int temp = 0;
            temp = nowState[clickedNumLoc];
            nowState[clickedNumLoc] = nowState[empLoc];
            nowState[empLoc] = temp;

            //盤面描画
            stateTile();

            if (!isCountInterpretation)
            {
                clickCount++;
            }
        }
        if (isCountInterpretation)
        {
            clickCount++;
        }

    }

    //盤面をシャッフルする関数
    public void Shuffle()
    {
        bool isParity = false;

        do
        {
            tentoCount = 0;

            //numbersにランダムに数字を代入
            for (int i = 0; i < numbers.Count; i++)
            {
                int randIndex = UnityEngine.Random.Range(i, numbers.Count);
                int temp = numbers[i];
                numbers[i] = numbers[randIndex];
                numbers[randIndex] = temp;
            }

            for (int i = 0; i <= tileNum; i++)
            {
                //オブジェクトをnumbersで指定されたランダムな位置に移動
                numberArray[i].transform.localPosition = numberTransform[numbers[i]];

                //パリティチェックに状態を順に代入（123～780の順でいれたいが他のリストが012～で用意されてるからめんどくさい処理）
                if (numbers[i] == 0)
                {
                    parityCheck[tileNum] = i;
                }
                else
                {
                    parityCheck[numbers[i] - 1] = i;
                }
            }

            //転倒数をカウント
            for (int i = 0; i <= tileNum; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (parityCheck[j] > parityCheck[i])
                    {
                        tentoCount++;
                    }
                }
            }

            //パリティ判定
            color = 0; //グリッド上で0=white 1=black
            for (int i = 1; i <= (int)Math.Sqrt(tileNum + 1); i++)
            {
                if ((numbers[0]) > (i * (int)Math.Sqrt(tileNum + 1)))
                {
                    continue;
                }
                else if ((numbers[0] == 0) && i < ((int)Math.Sqrt(tileNum + 1)))
                {
                    continue;
                }

                if ((i % 2 != 0))
                {
                    if ((numbers[0] % 2 != 0) || ((numbers[0] == 0)))
                    {
                        color = 1;
                        break;
                    }
                    else
                    {
                        color = 0;
                        break;
                    }

                }
                else
                {
                    if ((numbers[0] % 2 == 0) || ((numbers[0] == 0)))
                    {

                        if (tileNum % 2 != 0)
                        {
                            color = 1;
                            break;
                        }
                        else
                        {
                            color = 0;
                            break;
                        }
                    }
                    else
                    {

                        if (tileNum % 2 != 0)
                        {
                            color = 0;
                            break;
                        }
                        else
                        {
                            color = 1;
                            break;
                        }
                    }

                }

            }
            if ((tileNum % 2 == 0) && ((color == 1) && (tentoCount % 2 == 0)))
            {
                isParity = false;
            }
            else if ((tileNum % 2 != 0) && ((color == 1) && (tentoCount % 2 != 0)))
            {
                isParity = false;
            }
            else if ((tileNum % 2 == 0) && ((color == 0) && (tentoCount % 2 != 0)))
            {
                isParity = false;
            }
            else if ((tileNum % 2 != 0) && ((color == 0) && (tentoCount % 2 == 0)))
            {
                isParity = false;
            }
            else
            {
                isParity = true;
            }

        } while (isParity);//パリティなければ抜け出せる

        for (int i = 0; i <= tileNum; i++)
        {
            nowState[i] = parityCheck[i];
        }
    }

    //クリックされた際の具体的な処理
    public void clicked(GameObject Clicked)
    {

        Vector3 transformCheck = Clicked.transform.localPosition;//クリックされたオブジェクトの位置
        Vector3 empty = numberArray[0].transform.localPosition; //空のオブジェクトの位置
        bool isX = false;// X方向の移動かどうか
        bool isTimesTileInterval = false; // タイル間隔のn倍の位置にemptyがあるかどうか
        int adjNum = 0; //何個となりにあるか
        for (int i = 1; i <= 5; i++) //5は適当（5を超えることはまあないだろう）
        {
            //移動可能かどうかの判定(1方向)
            if ((((transformCheck.x) + (i * tileInterval)) == (empty.x)) || (((transformCheck.x) - (i * tileInterval)) == (empty.x)))
            {
                adjNum = i;
                isTimesTileInterval = true;
                break;
            }
            else if ((((transformCheck.z) + (i * tileInterval)) == (empty.z)) || (((transformCheck.z) - (i * tileInterval)) == (empty.z)))
            {
                adjNum = i;
                isTimesTileInterval = true;
                break;
            }
        }
        //移動可能かどうかの判定(完全)
        if (isTimesTileInterval && (transformCheck.z == empty.z))
        {
            isX = true;
            Swap(transformCheck.x, numberArray[0].transform.localPosition.x, Clicked, isX, adjNum);
            isTimesTileInterval = false;
        }
        else if (isTimesTileInterval && (transformCheck.x == empty.x))
        {
            isX = false;
            Swap(transformCheck.z, numberArray[0].transform.localPosition.z, Clicked, isX, adjNum);
            isTimesTileInterval = false;
        }
    }

    // タイルを配置する関数
    public void stateTile()
    {
        for (int i = 0; i <= tileNum; i++)
        {
            if (i != tileNum)
            {
                numberArray[nowState[i]].transform.localPosition = numberTransform[i + 1];
            }
            else
            {
                numberArray[nowState[i]].transform.localPosition = numberTransform[0];
            }

        }
    }
    public void stateTileIDA(Vector2Int[] nextPos,int k)
    {
        
        int count = 0;
        for(int i = 0; i < rowNum; i++)
        {
            for(int j = 0; j < rowNum; j++)
            {
                //Debug.Log(nextPos[count]);
                nextState[count] = Array.IndexOf(nextPos,new Vector2Int(i, j));
                count++;
            }
        }
        
        for (int i = 0; i <= tileNum; i++)
        {
            if (i != tileNum)
            {
                numberArray[nextState[i]].transform.localPosition = numberTransform[i + 1];
            }
            else
            {
                numberArray[nextState[i]].transform.localPosition = numberTransform[0];
            }

        }
        if(k!=0)clickCount++;
    }
}
