using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum EPathdataNodeAttributes : byte
{
    None     = 0x00,

    Walkable = 0x01,

    HasWater = 0x02,
   
    IsBoundary = 0x80
}

[System.Flags]
public enum ENeighbourFlags : byte
{
    None        = 0x00,

    North       = 0x01,
    NorthEast   = 0x02,
    East        = 0x04,
    SouthEast   = 0x08,
    South       = 0x10,
    SouthWest   = 0x20,
    West        = 0x40,
    NorthWest   = 0x80
}

public static class GridHelpers
{
    public static Vector3Int Step_North     = new Vector3Int(0, 1, 0);
    public static Vector3Int Step_NorthEast = new Vector3Int(1, 1, 0);
    public static Vector3Int Step_East      = new Vector3Int(1, 0, 0);
    public static Vector3Int Step_SouthEast = new Vector3Int(1, -1, 0);
    public static Vector3Int Step_South     = new Vector3Int(0, -1, 0);
    public static Vector3Int Step_SouthWest = new Vector3Int(-1, -1, 0);
    public static Vector3Int Step_West      = new Vector3Int(-1, 0, 0);
    public static Vector3Int Step_NorthWest = new Vector3Int(-1, 1, 0);
}

public class PathdataNode
{
    public Vector3 WorldPos;
    public Vector3Int GridPos;
    public EPathdataNodeAttributes Attributes;
    public ENeighbourFlags NeighbourFlags;

    public bool IsBoundary => Attributes.HasFlag(EPathdataNodeAttributes.IsBoundary);
    public bool IsWalkable => Attributes.HasFlag(EPathdataNodeAttributes.Walkable);
    public bool IsWater => Attributes.HasFlag(EPathdataNodeAttributes.HasWater);
}

[System.Serializable]
public class Pathdata : ScriptableObject, ISerializationCallbackReceiver
{
    [SerializeField] EResolution Resolution = EResolution.NodeSize_1x1;
    [SerializeField] Vector2Int Dimensions;
    [SerializeField] Vector3 CellSize;
    [SerializeField] EPathdataNodeAttributes[] Attributes;
    [SerializeField] ENeighbourFlags[] NeighbourFlags;
    [SerializeField] float[] Heights;

    [System.NonSerialized] public PathdataNode[] Nodes;

    public void Initialise(EResolution _Resolution, Vector2Int _Dimensions, Vector3 _CellSize)
    {
        Resolution = _Resolution;
        Dimensions = _Dimensions;
        CellSize = _CellSize;

        Nodes = new PathdataNode[Dimensions.x * Dimensions.y];     
        for (int index = 0; index < Nodes.Length; ++index)  
            Nodes[index] = new PathdataNode();
    }

    public void InitialiseNode(int row, int column, Vector3 worldPos, EPathdataNodeAttributes attributes)
    {
        int nodeIndex = column + (row * Dimensions.x);

        Nodes[nodeIndex].GridPos = new Vector3Int(column, row, 0);
        Nodes[nodeIndex].WorldPos = worldPos;
        Nodes[nodeIndex].Attributes = attributes;
    }

    public void OnAfterDeserialize()
    {
        // initialise the nodes
        Nodes = new PathdataNode[Attributes.Length];
        for (int index = 0; index < Attributes.Length; ++index)
        {
            Nodes[index] = new PathdataNode();

            Nodes[index].Attributes = Attributes[index];
            Nodes[index].NeighbourFlags = NeighbourFlags[index];

            int x = index % Dimensions.x;
            int y = (index - x) / Dimensions.x;
            Nodes[index].GridPos = new Vector3Int(x, y, 0);

            Nodes[index].WorldPos = new Vector3(y * CellSize.x, Heights[index], x * CellSize.z);
        }
    }

    public void OnBeforeSerialize()
    {
        if (Nodes == null || Nodes.Length == 0)
        {
            Attributes = null;
            NeighbourFlags = null;
            Heights = null;
            return;
        }

        // extract data from the nodes
        Attributes = new EPathdataNodeAttributes[Nodes.Length];
        NeighbourFlags = new ENeighbourFlags[Nodes.Length];
        Heights = new float[Nodes.Length];
        for (int index = 0; index < Attributes.Length; ++index)
        {
            Attributes[index] = Nodes[index].Attributes;
            NeighbourFlags[index] = Nodes[index].NeighbourFlags;
            Heights[index] = Nodes[index].WorldPos.y;
        }
    }

    public PathdataNode GetNode(Vector3Int gridPos)
    {
        return GetNode(gridPos.y, gridPos.x);
    }

    public PathdataNode GetNode(int row, int column)
    {
        if (row < 0 || column < 0 || row >= Dimensions.y || column >= Dimensions.x)
            return null;

        return Nodes[column + (row * Dimensions.x)];
    }
}
