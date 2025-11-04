using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class clickDetect : MonoBehaviour, IPointerClickHandler
{
    public int tile;//それぞれのタイル番号格納
    private Controller controller = null;//Contorllerスクリプト取得

    // Start is called before the first frame update
    void Start()
    {
        controller = GetComponentInParent<Controller>();
    }

    // Update is called once per frame
    void Update()
    {

    }
    public void OnPointerClick(PointerEventData eventData)
    {
        controller.clickedNum = tile;
        if (controller.isfinish == false)
        {
            controller.clicked(gameObject);//こっちからClickedNumをいじる
        }
        controller.clickedNum = 0;
    }
}
