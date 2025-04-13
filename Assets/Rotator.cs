using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotator : MonoBehaviour
{
    float x, y, z;
    // Start is called before the first frame update
    void Start()
    {
        x = transform.rotation.x;
        y = transform.rotation.y;
        z = transform.rotation.z;
    }

    // Update is called once per frame
    void Update()
    {
        y += Time.deltaTime * 20;
        gameObject.transform.rotation = Quaternion.Euler(x,y,z);
    }
}
