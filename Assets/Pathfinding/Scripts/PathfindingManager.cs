using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathfindingContext
{
    [System.Flags]
    enum ENodeStatus
    {
        None    = 0x00,
        Open    = 0x01,
        Closed  = 0x02
    }

    ENodeStatus[] Statuses;
    float[] GCosts;
    float[] HCosts;
    int[] ParentIDs;
    public int NumIterations { get; private set; }= 0;

    List<PathdataNode> OpenList = new List<PathdataNode>();

    public bool OpenListNotEmpty => OpenList.Count > 0;

    public PathfindingContext(int numNodes)
    {
        Statuses = new ENodeStatus[numNodes];
        GCosts = new float[numNodes];
        HCosts = new float[numNodes];
        ParentIDs = new int[numNodes];

        // initialise our data
        for (int index = 0; index < numNodes; ++index)
        {
            GCosts[index] = HCosts[index] = float.MaxValue;
            ParentIDs[index] = -1;
        }
    }

    public void OpenNode(PathdataNode node, float gCost, float hCost, int parentID)
    {
        OpenList.Add(node);

        Statuses[node.UniqueID] = ENodeStatus.Open;
        GCosts[node.UniqueID] = gCost;
        HCosts[node.UniqueID] = hCost;
        ParentIDs[node.UniqueID] = parentID;
    }

    public void UpdateOpenNode(PathdataNode node, float gCost, int parentID)
    {
        GCosts[node.UniqueID] = gCost;
        ParentIDs[node.UniqueID] = parentID;
    }

    public void MoveToClosed(PathdataNode node)
    {
        OpenList.Remove(node);
        Statuses[node.UniqueID] = ENodeStatus.Closed;
    }

    public float GetGCost(PathdataNode node)
    {
        return GCosts[node.UniqueID];
    }

    public float GetFCost(PathdataNode node)
    {
        return GCosts[node.UniqueID] + HCosts[node.UniqueID];
    }

    public int GetParentID(PathdataNode node)
    {
        return ParentIDs[node.UniqueID];
    }

    public bool IsNodeOpen(PathdataNode node)
    {
        return Statuses[node.UniqueID] == ENodeStatus.Open;
    }

    public bool IsNodeClosed(PathdataNode node)
    {
        return Statuses[node.UniqueID] == ENodeStatus.Closed;
    }

    public PathdataNode GetBestNode()
    {
        ++NumIterations;

        float bestFCost = float.MaxValue;
        PathdataNode bestNode = null;
        for (int index = 0; index < OpenList.Count; ++index)
        {
            float nodeCost = GetFCost(OpenList[index]);

            if (nodeCost < bestFCost)
            {
                bestFCost = nodeCost;
                bestNode = OpenList[index];
            }
        }

        return bestNode;
    }
}

public class PathfindingManager : MonoBehaviour
{
    public static PathfindingManager Instance { get; private set; } = null;

    public string PathdataUID;
    public Transform StartMarker;
    public Transform EndMarker;

    void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("Found duplicate PathfindingManager on " + gameObject.name);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        System.Diagnostics.Stopwatch pathfindingTimer = new System.Diagnostics.Stopwatch();

        pathfindingTimer.Start();
        var path = RequestPath(PathdataUID, StartMarker.position, EndMarker.position, 
                    delegate (PathdataNode current, PathdataNode destination)
                    {
                        return Vector3.Distance(current.WorldPos, destination.WorldPos);
                    });
        pathfindingTimer.Stop();

        Debug.Log(pathfindingTimer.ElapsedMilliseconds);

        foreach(var pathNode in path)
        {
            Debug.DrawLine(pathNode.WorldPos, pathNode.WorldPos + 5f * Vector3.up, Color.magenta, 600f);
        }
    }

    // Update is called once per frame
    void Update()
    {
    }

    ENeighbourFlags[] NeighboursToCheck = new ENeighbourFlags[] {
        ENeighbourFlags.North,
        ENeighbourFlags.NorthEast,
        ENeighbourFlags.East,
        ENeighbourFlags.SouthEast,
        ENeighbourFlags.South,
        ENeighbourFlags.SouthWest,
        ENeighbourFlags.West,
        ENeighbourFlags.NorthWest
    };

    Vector3Int[] NeighbourOffsets = new Vector3Int[] {
        GridHelpers.Step_North,
        GridHelpers.Step_NorthEast,
        GridHelpers.Step_East,
        GridHelpers.Step_SouthEast,
        GridHelpers.Step_South,
        GridHelpers.Step_SouthWest,
        GridHelpers.Step_West,
        GridHelpers.Step_NorthWest
    };

    public List<PathdataNode> RequestPath(string pathDataUID, Vector3 startPos, Vector3 endPos, System.Func<PathdataNode, PathdataNode, float> calculateCost)
    {
        // retrieve the pathdata
        var pathdata = PathdataManager.Instance.GetPathdata(pathDataUID);
        if (pathdata == null)
        {
            Debug.LogError("Could not retrieve pathdata " + pathDataUID);
            return null;
        }

        // retrieve the start node
        var startNode = pathdata.GetNode(startPos);
        if (startNode == null)
        {
            Debug.LogError("Failed to retrieve node at " + startPos);
            return null;
        }

        // retrieve the end node
        var endNode = pathdata.GetNode(endPos);
        if (endNode == null)
        {
            Debug.LogError("Failed to retrieve node at " + endPos);
            return null;
        }

        // check the area ids
        if (startNode.AreaID != endNode.AreaID || startNode.AreaID < 1 || endNode.AreaID < 1)
        {
            Debug.Log("No path exists");
            return null;
        }

        // setup the context
        PathfindingContext context = new PathfindingContext(pathdata.Nodes.Length);

        // open the start node
        context.OpenNode(startNode, 0f, calculateCost(startNode, endNode), -1);

        // loop while we have nodes to explore
        while (context.OpenListNotEmpty)
        {
            PathdataNode bestNode = context.GetBestNode();

            // reached destination?
            if (bestNode == endNode)
            {
                List<PathdataNode> foundPath = new List<PathdataNode>();

                while (bestNode != null)
                {
                    foundPath.Insert(0, bestNode);

                    bestNode = pathdata.GetNode(context.GetParentID(bestNode));
                }

                Debug.Log("Path found in " + context.NumIterations + " iterations");
                return foundPath;
            }

            // move to the closed list
            context.MoveToClosed(bestNode);

            for (int neighbourIndex = 0; neighbourIndex < NeighboursToCheck.Length; ++neighbourIndex)
            {
                // no neighbour exists
                if (!bestNode.NeighbourFlags.HasFlag(NeighboursToCheck[neighbourIndex]))
                    continue;

                PathdataNode neighbour = pathdata.GetNode(bestNode.GridPos + NeighbourOffsets[neighbourIndex]);

                // ignore if closed
                if (context.IsNodeClosed(neighbour))
                    continue;

                // is the node open?
                if (context.IsNodeOpen(neighbour))
                {
                    // calculate the cost to reach the neighbour
                    float gCost = context.GetGCost(bestNode) + calculateCost(bestNode, neighbour);

                    // have we found a shorter path?
                    if (gCost < context.GetGCost(neighbour))
                        context.UpdateOpenNode(neighbour, gCost, bestNode.UniqueID);
                }
                else
                {
                    // calculate the cost to reach the neighbour
                    float gCost = context.GetGCost(bestNode) + calculateCost(bestNode, neighbour);

                    context.OpenNode(neighbour, gCost, calculateCost(neighbour, endNode), bestNode.UniqueID);
                }
            }
        }

        Debug.LogError("No path could be found");

        return null;
    }
}
