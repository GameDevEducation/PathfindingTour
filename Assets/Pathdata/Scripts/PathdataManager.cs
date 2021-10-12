using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR

public enum EResolution
{
    NodeSize_1x1 = 1,
    NodeSize_2x2 = 2,
    NodeSize_4x4 = 4,
    NodeSize_8x8 = 8,
    NodeSize_16x16 = 16
}

[System.Serializable]
public class PathdataSet
{
    public EResolution Resolution = EResolution.NodeSize_1x1;
    public float SlopeLimit = 45f;

    public Pathdata Data;
}

public class PathdataManager : MonoBehaviour
{
    [SerializeField] List<PathdataSet> PathdataSets;
    [SerializeField] Terrain Source_Terrain;
    [SerializeField] Texture2D Source_BiomeMap;
    [SerializeField] Texture2D Source_SlopeMap;

    [SerializeField] float WaterHeight = 15f;

    [SerializeField] bool Debug_ShowNodes = false;
    [SerializeField] bool Debug_ShowEdges = false;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    #if UNITY_EDITOR
    public void OnDrawGizmos()
    {
        Vector3 cameraLocation = SceneView.currentDrawingSceneView.camera.transform.position;

        for (int setIndex = 0; setIndex < PathdataSets.Count; ++setIndex)
        {
            var pathdataSet = PathdataSets[setIndex];

            // pathdata not yet built
            if (pathdataSet.Data == null)
                continue;

            for (int nodeIndex = 0; nodeIndex < pathdataSet.Data.Nodes.Length; ++nodeIndex)
            {
                var node = pathdataSet.Data.Nodes[nodeIndex];

                if (node == null)
                    continue;

                if ((node.WorldPos - cameraLocation).sqrMagnitude > (50 * 50))
                    continue;

                DrawDebug(node);
            }
        }
    }

    void DrawDebug(PathdataNode node)
    {
        if (node.IsBoundary)
            return;

        if (Debug_ShowNodes)
        {
            if (node.IsWalkable)
                Gizmos.color = Color.green;
            else if (node.IsWater)
                Gizmos.color = Color.blue;
            else
                Gizmos.color = Color.red;

            Gizmos.DrawLine(node.WorldPos, node.WorldPos + Vector3.up);
        }

        if (Debug_ShowEdges)
        {
            Gizmos.color = Color.white;
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.North))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(0, 1, 1));
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.NorthEast))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(1, 1, 1));
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.East))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(1, 1, 0));
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.SouthEast))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(1, 1, -1));
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.South))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(0, 1, -1));
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.SouthWest))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(-1, 1, -1));
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.West))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(-1, 1, 0));
            if (node.NeighbourFlags.HasFlag(ENeighbourFlags.NorthWest))
                Gizmos.DrawLine(node.WorldPos, node.WorldPos + new Vector3(-1, 1, 1));
        }
    }

    public void OnEditorBuildPathdata()
    {
        Internal_BuildPathdata();
    }
    #endif // UNITY_EDITOR

    void Internal_BuildPathdata()
    {
        for (int index = 0; index < PathdataSets.Count; ++index)
            Internal_BuildPathdata(PathdataSets[index], index);
    }

    void Internal_BuildPathdata(PathdataSet pathdataSet, int pathdataIndex)
    {
        // allocate the pathdata
        var pathdata = ScriptableObject.CreateInstance<Pathdata>();

#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(pathdata, "Create pathdata");

        string fileName = SceneManager.GetActiveScene().name + "_Pathdata_" + pathdataIndex;
        string assetPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SceneManager.GetActiveScene().path), fileName + ".asset");
        System.IO.File.Delete(assetPath);

        AssetDatabase.CreateAsset(pathdata, assetPath);
#endif // UNITY_EDITOR        

        pathdataSet.Data = pathdata;

        var pathdataSize = Source_Terrain.terrainData.heightmapResolution;
        var heightMapScale = Source_Terrain.terrainData.heightmapScale;
        pathdata.Initialise(pathdataSet.Resolution, new Vector2Int(pathdataSize, pathdataSize), heightMapScale);

        Internal_BuildPathdata(pathdata, pathdataSet);

#if UNITY_EDITOR
        // flag the asset as dirty and save it
        EditorUtility.SetDirty(pathdata);
        AssetDatabase.SaveAssets();
#endif // UNITY_EDITOR          
    }

    void Internal_BuildPathdata(Pathdata pathdata, PathdataSet configuration)
    {
        // extract key data
        var pathdataSize = Source_Terrain.terrainData.heightmapResolution;
        var heightMapScale = Source_Terrain.terrainData.heightmapScale;
        var heightMap = Source_Terrain.terrainData.GetHeights(0, 0, pathdataSize, pathdataSize);
        var cosSlopeLimit = Mathf.Cos(configuration.SlopeLimit * Mathf.Deg2Rad);

        // process the height map
        for (int row = 0; row < pathdataSize; ++row)
        {
            float textureU = (float)row / (float)pathdataSize;
            
            for (int column = 0; column < pathdataSize; ++column)
            {
                float textureV = (float)column / (float)pathdataSize;

                // generate world pos
                Vector3 worldPos = new Vector3(row * heightMapScale.z, heightMap[column, row] * heightMapScale.y, column * heightMapScale.x);

                // determine the slope
                float cosSlope = Source_SlopeMap.GetPixelBilinear(textureU, textureV).r;

                // build the attributes
                EPathdataNodeAttributes attributes = EPathdataNodeAttributes.None;

                if (worldPos.y < WaterHeight)
                    attributes |= EPathdataNodeAttributes.HasWater;
                else if (cosSlope >= cosSlopeLimit)
                    attributes |= EPathdataNodeAttributes.Walkable;

                if (row == 0 || column == 0 || row == (pathdataSize - 1) || column == (pathdataSize - 1))
                    attributes = EPathdataNodeAttributes.IsBoundary;

                // initialise the node
                pathdata.InitialiseNode(row, column, worldPos, attributes);
            }
        }

        // build up the neighbours
        for (int row = 0; row < pathdataSize; ++row)
        {
            for (int column = 0; column < pathdataSize; ++column)
            {
                var node = pathdata.GetNode(row, column);

                // boundary nodes do not connect
                if (node.IsBoundary)
                    continue;

                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.North, GridHelpers.Step_North);
                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.NorthEast, GridHelpers.Step_NorthEast);
                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.East, GridHelpers.Step_East);
                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.SouthEast, GridHelpers.Step_SouthEast);
                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.South, GridHelpers.Step_South);
                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.SouthWest, GridHelpers.Step_SouthWest);
                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.West, GridHelpers.Step_West);
                UpdateNeighbourFlags(node, pathdata, ENeighbourFlags.NorthWest, GridHelpers.Step_NorthWest);
            }
        }
    }

    void UpdateNeighbourFlags(PathdataNode currentNode, Pathdata pathdata, 
                              ENeighbourFlags directionFlag, Vector3Int offset)
    {
        var neighbour = pathdata.GetNode(currentNode.GridPos + offset);

        // no neighbour or it's a boundary so do nothing
        if (neighbour == null || neighbour.IsBoundary)
            return;

        // link only if both the same
        if ((neighbour.IsWalkable && currentNode.IsWalkable) || (neighbour.IsWater && currentNode.IsWater))
            currentNode.NeighbourFlags |= directionFlag;
    }
}
