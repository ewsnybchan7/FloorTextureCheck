/*
 * TerrainDataExtractorTool.cs
 * 작성자: ewsnybchan7
 * 작성일: 2022.09.07 16:38:22
 */

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldMapDataBaker
{
    public partial class WorldMapDataBakerEditor
    {
        #region Cached Terrain Data
        
        private TerrainData m_TerrainData = null;
        private TerrainLayer[] m_TerrainLayerList = null;
        private int m_TerrainLayerLength = 0;

        private int m_TerrainWidth = 0;
        private int m_TerrainLength = 0;

        private int m_AlphaMapWidth = 0;
        private int m_AlphaMapHeight = 0;
        private float[,,] m_AlphaMap = null;
        
        #endregion

        #region Export Variable
        
        private float m_CellSize = 1f;
        private AutoDictionary<string, EFLOOR_TYPE> m_TerrainTypeTable = new();
        private EFLOOR_TYPE[,] m_TerrainLookupTable = null;

        private static readonly string EXPORTED_TERRAIN_DATA_FILE_NAME = "Assets/Editor/Terrain/TerrainDataExtractor/Exported/TerrainData/{0}.bytes";

        #endregion

        private void OnEnableTerrainData() { }

        #region GUI

        private void OnGUITerrainData()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false));
            
            var terrainData = (TerrainData)EditorGUILayout.ObjectField(m_TerrainData, typeof(TerrainData), true);
            if (terrainData != m_TerrainData)
            {
                m_TerrainData = terrainData;
                if (m_TerrainData != null)
                    SetupTerrainInfo();
            }

            EditorGUILayout.EndVertical();
        }

        private void OnGUICollectedTerrainData()
        {
            if (m_TerrainData == null)
                return;

            EditorGUILayout.BeginVertical(GUILayout.Width(CONTENT_WIDTH), GUILayout.ExpandWidth(false));
            if (GUILayout.Button("Collect Terrain Data"))
            {
                OnClicked_CollectData(EEXPORT_TYPE.TERRAIN_DATA);
            }

            if (m_TerrainLayerList.IsNullOrEmpty() == false)
            {
                foreach (var terrainLayer in m_TerrainData.terrainLayers)
                {
                    EditorGUILayout.BeginHorizontal();

                    EditorGUILayout.LabelField("", "", GUILayout.Height(45f), GUILayout.Width(45f), GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                    var rect = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawPreviewTexture(rect, terrainLayer.diffuseTexture);

                    EditorGUILayout.LabelField(terrainLayer.name);

                    m_TerrainTypeTable[terrainLayer.name] = (EFLOOR_TYPE)EditorGUILayout.EnumPopup(m_TerrainTypeTable[terrainLayer.name], GUILayout.Width(100f), GUILayout.ExpandWidth(false));

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        #endregion
        
        private void ClearTerrainDataInfo()
        {
            
        }
        
        private void SetupTerrainInfo()
        {
            m_TerrainWidth = (int)(m_TerrainData.size.x / m_CellSize);
            m_TerrainLength = (int)(m_TerrainData.size.z / m_CellSize);

            m_TerrainLayerLength = m_TerrainData.terrainLayers.Length;
            
            m_TerrainLookupTable = new EFLOOR_TYPE[m_TerrainLength, m_TerrainWidth];

            m_AlphaMapWidth = m_TerrainData.alphamapWidth;
            m_AlphaMapHeight = m_TerrainData.alphamapHeight;
        }

        private void OnCollectData_TerrainData()
        {
            CollectTerrainLayerTexture();
        }
        
        private void CollectTerrainLayerTexture()
        {
            m_TerrainLayerList = m_TerrainData.terrainLayers;
        }
        
        private void OnExport_TerrainData()
        {
            try
            {
                m_AlphaMap = m_TerrainData.GetAlphamaps(0, 0, m_AlphaMapWidth, m_AlphaMapHeight);
                m_TerrainLookupTable = new EFLOOR_TYPE[m_AlphaMapHeight, m_AlphaMapWidth];

                ConvertTerrainLayerToType();
                BakeTerrainDataFile();
            }
            catch (Exception e)
            {
                RM2Logger.LogError(e.ToString());
            }
        }
        
        private void ConvertTerrainLayerToType()
        {
            ForeachTerrainAlpha(InternalConvert);

            void InternalConvert(int InX, int InY)
            {
                var max = 0f;
                var index = 0;

                for (int i = 0; i < m_TerrainLayerLength; ++i)
                {
                    var value = Math.Max(max, m_AlphaMap[InY, InX, i]);
                    if (value != max)
                    {
                        index = i;
                    }
                }

                m_TerrainLookupTable[InY, InX] = m_TerrainTypeTable[m_TerrainLayerList[index].name];
            }
        }
        
        private void BakeTerrainDataFile()
        {
            if (m_TerrainLookupTable is {Length: <= 0})
                return;

            var filePath = string.Format(EXPORTED_TERRAIN_DATA_FILE_NAME, m_TerrainData.name);
            
            using OneTimeWatchTimer stopwatch = new($"{m_TerrainData.name} Convert");
            try
            {
                using FileStream stream = new(filePath, FileMode.Create);
                BinaryWriter writer = new(stream);

                ForeachTerrainAlpha((x, y) =>
                {
                    writer.Write((byte)m_TerrainLookupTable[y, x]);
                });

                writer.Close();
            }
            catch (Exception e)
            {
                RM2Logger.LogError($"{m_TerrainData.name} Terrain Data Convert Failed: {e}");
            }
        }

        private bool ReadBakedTerrainDataFile()
        {
            var filePath = string.Format(EXPORTED_TERRAIN_DATA_FILE_NAME, m_TerrainData.name);
            
            FileInfo fileInfo = new(filePath);
            if (fileInfo.Exists == false)
                return false;
            
            try
            {
                using FileStream fileStream = new(filePath, FileMode.Open);
                BinaryReader reader = new(fileStream);
                
                ForeachTerrainAlpha((x, y) =>
                {
                    var value = reader.ReadByte();
                    PREVIEW_TEXTURE.SetPixel(x, y, GetColorFromFloorType((EFLOOR_TYPE)value));
                });
                
                
                reader.Close();
                PREVIEW_TEXTURE.Apply();
                return true;
            }
            catch (Exception e)
            {
                RM2Logger.LogError($"{m_TerrainData.name} TerrainData Read Fail: {e}]");
                return false;
            }
        }

        private void ForeachTerrainAlpha(Action<int, int> InAction)
        {
            for (int y = 0; y < m_AlphaMapHeight; ++y)
            {
                for (int x = 0; x < m_AlphaMapWidth; ++x)
                {
                    InAction?.Invoke(x, y);
                }
            }
        }
    }
}