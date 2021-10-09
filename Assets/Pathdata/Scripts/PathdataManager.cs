using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PathdataSet
{
    public Pathdata Data;
}

public class PathdataManager : MonoBehaviour
{
    [SerializeField] List<PathdataSet> PathdataSets;
    [SerializeField] Terrain Source_Terrain;
    [SerializeField] Texture2D Source_BiomeMap;
    [SerializeField] Texture2D Source_SlopeMap;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    #if UNITY_EDITOR
    public void OnEditorBuildPathdata()
    {
        Internal_BuildPathdata();
    }
    #endif // UNITY_EDITOR

    void Internal_BuildPathdata()
    {
        foreach(var pathdataSet in PathdataSets)
            Internal_BuildPathdata(pathdataSet);
    }

    void Internal_BuildPathdata(PathdataSet pathdataSet)
    {

    }
}
