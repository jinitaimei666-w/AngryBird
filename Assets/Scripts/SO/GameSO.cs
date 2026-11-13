using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu()]
public class GameSO : ScriptableObject
{
    public MapSO[] mapArray;
    public int selectMapID = -1;
    public int selectedLevelID = -1;
    public void UpdateLevel(int number)
    {
        if (number <= 0) return;
        if (number > mapArray[selectMapID-1].starNumberOfLevel[selectedLevelID-1])
        {
            mapArray[selectMapID-1].starNumberOfLevel[selectedLevelID-1]=number;
            int sum = 0;
            foreach(int num in mapArray[selectMapID-1].starNumberOfLevel)
            {
                if (num > 0)
                {
                    sum += num;
                }
            }
            mapArray[selectMapID-1].starNumberOfMap = sum;

            if (selectedLevelID >= mapArray[selectMapID - 1].starNumberOfLevel.Length)
            {
                if(selectMapID<mapArray.Length&&mapArray[selectMapID ].starNumberOfMap == -1)
                {
                    mapArray[selectMapID].starNumberOfMap = 0;
                    mapArray[selectMapID].starNumberOfLevel[0] = 0;
                }
            }
            else
            {
                if(mapArray[selectMapID - 1].starNumberOfLevel[selectedLevelID] == -1)
                {
                    mapArray[selectMapID - 1].starNumberOfLevel[selectedLevelID] = 0;
                }
            }
        }
    }

}
