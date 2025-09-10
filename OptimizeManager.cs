using System.Collections.Generic;
using UnityEngine;
public class OptimizeManager : MonoBehaviour
{
    [SerializeField] private float minimumDistance = 1000f;
    [SerializeField] private float checkInterval = 1f;

    private Transform player;
    private List<GameObject> treeList = new List<GameObject>();
    private float nextCheckTime = 0f;

    private void Start()
    {
        var playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
        RefreshTreeList();
    }

    private void Update()
    {
        if (player == null) return;

        if (Time.time >= nextCheckTime)
        {
            RefreshTreeList();
            nextCheckTime = Time.time + checkInterval;
        }

        Vector3 playerPos = player.position;
        for (int i = 0; i < treeList.Count; i++)
        {
            GameObject tree = treeList[i];
            if (tree == null) continue;

            float distance = Vector3.SqrMagnitude(playerPos - tree.transform.position);
            bool shouldBeActive = distance < minimumDistance * minimumDistance;
            if (tree.activeSelf != shouldBeActive)
                tree.SetActive(shouldBeActive);
        }
    }

    private void RefreshTreeList()
    {
        treeList.Clear();
        GameObject[] allTrees = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject obj in allTrees)
        {
            if (obj.CompareTag("Tree") && obj.scene.IsValid())
            {
                treeList.Add(obj);
            }
        }
    }
}