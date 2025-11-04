using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class constCtr : MonoBehaviour
{
    private Controller ctr;

    // Start is called before the first frame update
    void Start()
    {
        ctr = GetComponent<Controller>();
        if(this.name.Equals($"Stage {8}"))
        {
            
            ctr.tileInterval = 0.323f;
            ctr.tileNum = 8;
            
        }
        else if (this.name.Equals($"Stage {15}"))
        {
            
            ctr.tileInterval = 0.25f;
            ctr.tileNum = 15;
            
        }
        
    }

    public void setTransArray8()
    {
        float tileInterval;
        float thickness = ctr.thickness;
        tileInterval = ctr.tileInterval;
        ctr.numberTransform[0] = new Vector3(tileInterval, thickness, -tileInterval);
        ctr.numberTransform[1] = new Vector3(-tileInterval, thickness, tileInterval);
        ctr.numberTransform[2] = new Vector3(0.0f, thickness, tileInterval);
        ctr.numberTransform[3] = new Vector3(tileInterval, thickness, tileInterval);
        ctr.numberTransform[4] = new Vector3(-tileInterval, thickness, 0.0f);
        ctr.numberTransform[5] = new Vector3(0.0f, thickness, 0.0f);
        ctr.numberTransform[6] = new Vector3(tileInterval, thickness, 0.0f);
        ctr.numberTransform[7] = new Vector3(-tileInterval, thickness, -tileInterval);
        ctr.numberTransform[8] = new Vector3(-0.0f, thickness, -tileInterval);
    }

    //àÍâû15ópÇÃÇ‡çÏÇ¡ÇƒÇ†ÇÈ
    public void setTransArray15()
    {
        float tileInterval;
        float thickness = ctr.thickness;
        tileInterval = ctr.tileInterval;
        ctr.numberTransform[0] = new Vector3(1.5f * tileInterval, thickness, -1.5f * tileInterval);
        ctr.numberTransform[1] = new Vector3(-1.5f * tileInterval, thickness, 1.5f * tileInterval);
        ctr.numberTransform[2] = new Vector3(-0.5f * tileInterval, thickness, 1.5f * tileInterval);
        ctr.numberTransform[3] = new Vector3(0.5f * tileInterval, thickness, 1.5f * tileInterval);
        ctr.numberTransform[4] = new Vector3(1.5f * tileInterval, thickness, 1.5f * tileInterval);
        ctr.numberTransform[5] = new Vector3(-1.5f * tileInterval, thickness, 0.5f * tileInterval);
        ctr.numberTransform[6] = new Vector3(-0.5f * tileInterval, thickness, 0.5f * tileInterval);
        ctr.numberTransform[7] = new Vector3(0.5f * tileInterval, thickness, 0.5f * tileInterval);
        ctr.numberTransform[8] = new Vector3(1.5f * tileInterval, thickness, 0.5f * tileInterval);
        ctr.numberTransform[9] = new Vector3(-1.5f * tileInterval, thickness, -0.5f * tileInterval);
        ctr.numberTransform[10] = new Vector3(-0.5f * tileInterval, thickness, -0.5f * tileInterval);
        ctr.numberTransform[11] = new Vector3(0.5f * tileInterval, thickness, -0.5f * tileInterval);
        ctr.numberTransform[12] = new Vector3(1.5f * tileInterval, thickness, -0.5f * tileInterval);
        ctr.numberTransform[13] = new Vector3(-1.5f * tileInterval, thickness, -1.5f * tileInterval);
        ctr.numberTransform[14] = new Vector3(-0.5f * tileInterval, thickness, -1.5f * tileInterval);
        ctr.numberTransform[15] = new Vector3(0.5f * tileInterval, thickness, -1.5f * tileInterval);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
