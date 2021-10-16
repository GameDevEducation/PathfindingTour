using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PathfindTester : MonoBehaviour
{
    public string PathdataUID;
    public Transform StartMarker;
    public Transform EndMarker;

    System.Diagnostics.Stopwatch asyncPathfindTimer = new System.Diagnostics.Stopwatch();

    // Start is called before the first frame update
    void Start()
    {
        TestSynchronous();

        asyncPathfindTimer.Start();
        PathfindingManager.Instance.RequestPath_Asynchronous(PathdataUID, StartMarker.position, EndMarker.position,
                    delegate (PathdataNode current, PathdataNode destination)
                    {
                        return Vector3.Distance(current.WorldPos, destination.WorldPos);
                    },
                    OnAsyncPathfindComplete);
    }

    void OnAsyncPathfindComplete(List<PathdataNode> path, EPathfindResult result)
    {
        asyncPathfindTimer.Stop();
        Debug.Log("Async Pathfind: " + asyncPathfindTimer.ElapsedMilliseconds);

        if (path != null)
        {
            foreach (var pathNode in path)
            {
                Debug.DrawLine(pathNode.WorldPos, pathNode.WorldPos + 5f * Vector3.up + Vector3.forward, Color.cyan, 600f);
            }
        }
    }

    void TestSynchronous()
    {
        System.Diagnostics.Stopwatch syncPathfindTimer = new System.Diagnostics.Stopwatch();

        syncPathfindTimer.Start();
        List<PathdataNode> path;
        var result = PathfindingManager.Instance.RequestPath_Synchronous(PathdataUID, StartMarker.position, EndMarker.position,
                    delegate (PathdataNode current, PathdataNode destination)
                    {
                        return Vector3.Distance(current.WorldPos, destination.WorldPos);
                    },
                    out path);
        syncPathfindTimer.Stop();

        Debug.Log("Sync Pathfind: " + syncPathfindTimer.ElapsedMilliseconds);

        if (path != null)
        {
            foreach (var pathNode in path)
            {
                Debug.DrawLine(pathNode.WorldPos, pathNode.WorldPos + 5f * Vector3.up, Color.magenta, 600f);
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
