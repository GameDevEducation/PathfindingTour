using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;
#endif // UNITY_EDITOR

public class ProcGenData
{
    public byte[,] BiomeMap_LowResolution;
    public float[,] BiomeStrengths_LowResolution;

    public byte[,] BiomeMap;
    public float[,] BiomeStrengths;

    public float[,] SlopeMap;
}

public class ProcGenManager : MonoBehaviour
{
    [SerializeField] ProcGenConfigSO Config;
    [SerializeField] Terrain TargetTerrain;
    [SerializeField] Texture2D Output_BiomeMap;
    [SerializeField] Texture2D Output_SlopeMap;

    ProcGenData WorkingData;

    Dictionary<TextureConfig, int> BiomeTextureToTerrainLayerIndex = new Dictionary<TextureConfig, int>();

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

#if UNITY_EDITOR
    public void RegenerateTextures()
    {
        Perform_LayerSetup();    
    }
    
    public void RegenerateWorld()
    {
        Undo.RecordObject(this, "Regenerate World");

        WorkingData = new ProcGenData();

        // cache the map resolution
        int mapResolution = TargetTerrain.terrainData.heightmapResolution;
        int alphaMapResolution = TargetTerrain.terrainData.alphamapResolution;

        // clear out any previously spawned objects
        for (int childIndex = transform.childCount - 1; childIndex >= 0; --childIndex)
        {
            Undo.DestroyObjectImmediate(transform.GetChild(childIndex).gameObject);
        }

        // Generate the texture mapping
        Perform_GenerateTextureMapping();

        // generate the low resolution biome map
        Perform_BiomeGeneration_LowResolution((int)Config.BiomeMapResolution);

        // generate the high resolution biome map
        Perform_BiomeGeneration_HighResolution((int)Config.BiomeMapResolution, mapResolution);

        // update the terrain heights
        Perform_HeightMapModification(mapResolution, alphaMapResolution);

        // paint the terrain
        Perform_TerrainPainting(mapResolution, alphaMapResolution);

        // place the objects
        Perform_ObjectPlacement(mapResolution, alphaMapResolution);
    }

    void Perform_GenerateTextureMapping()
    {
        BiomeTextureToTerrainLayerIndex.Clear();

        // build up list of all textures
        List<TextureConfig> allTextures = new List<TextureConfig>();
        foreach(var biomeMetadata in Config.Biomes)
        {
            List<TextureConfig> biomeTextures = biomeMetadata.Biome.RetrieveTextures();

            if (biomeTextures == null || biomeTextures.Count == 0)
                continue;

            allTextures.AddRange(biomeTextures);
        }

        if (Config.PaintingPostProcessingModifier != null)
        {
            // extract all textures from every painter
            BaseTexturePainter[] allPainters = Config.PaintingPostProcessingModifier.GetComponents<BaseTexturePainter>();
            foreach(var painter in allPainters)
            {
                var painterTextures = painter.RetrieveTextures();

                if (painterTextures == null || painterTextures.Count == 0)
                    continue;

                allTextures.AddRange(painterTextures);
            }            
        }

        // filter out any duplicate entries
        allTextures = allTextures.Distinct().ToList();

        // iterate over the texture configs
        int layerIndex = 0;
        foreach(var textureConfig in allTextures)
        {
            BiomeTextureToTerrainLayerIndex[textureConfig] = layerIndex;
            ++layerIndex;
        }
    }

    void Perform_LayerSetup()
    {
        // delete any existing layers
        if (TargetTerrain.terrainData.terrainLayers != null || TargetTerrain.terrainData.terrainLayers.Length > 0)
        {
            Undo.RecordObject(TargetTerrain, "Clearing previous layers");

            // build up list of asset paths for each layer
            List<string> layersToDelete = new List<string>();
            foreach(var layer in TargetTerrain.terrainData.terrainLayers)
            {
                if (layer == null)
                    continue;

                layersToDelete.Add(AssetDatabase.GetAssetPath(layer.GetInstanceID()));
            }

            // remove all links to layers
            TargetTerrain.terrainData.terrainLayers = null;

            // delete each layer
            foreach(var layerFile in layersToDelete)
            {
                if (string.IsNullOrEmpty(layerFile))
                    continue;

                AssetDatabase.DeleteAsset(layerFile);
            }

            Undo.FlushUndoRecordObjects();
        }

        string scenePath = System.IO.Path.GetDirectoryName(SceneManager.GetActiveScene().path);

        Perform_GenerateTextureMapping();

        // generate all of the layers
        int numLayers = BiomeTextureToTerrainLayerIndex.Count;
        List<TerrainLayer> newLayers = new List<TerrainLayer>(numLayers);
        
        // preallocate the layers
        for (int layerIndex = 0; layerIndex < numLayers; ++layerIndex)
        {
            newLayers.Add(new TerrainLayer());
        }

        // iterate over the texture map
        foreach(var textureMappingEntry in BiomeTextureToTerrainLayerIndex)
        {
            var textureConfig = textureMappingEntry.Key;
            var textureLayerIndex = textureMappingEntry.Value;
            var textureLayer = newLayers[textureLayerIndex];

            // configure the terrain layer textures
            textureLayer.diffuseTexture = textureConfig.Diffuse;
            textureLayer.normalMapTexture = textureConfig.NormalMap;

            // save as asset
            string layerPath = System.IO.Path.Combine(scenePath, "Layer_" + textureLayerIndex);
            AssetDatabase.CreateAsset(textureLayer, layerPath);
        }

        Undo.RecordObject(TargetTerrain.terrainData, "Updating terrain layers");
        TargetTerrain.terrainData.terrainLayers = newLayers.ToArray();
    }

    void Perform_BiomeGeneration_LowResolution(int mapResolution)
    {
        // allocate the biome map and strength map
        WorkingData.BiomeMap_LowResolution = new byte[mapResolution, mapResolution];
        WorkingData.BiomeStrengths_LowResolution = new float[mapResolution, mapResolution];

        // setup space for the seed points
        int numSeedPoints = Mathf.FloorToInt(mapResolution * mapResolution * Config.BiomeSeedPointDensity);
        List<byte> biomesToSpawn = new List<byte>(numSeedPoints);

        // populate the biomes to spawn based on weightings
        float totalBiomeWeighting = Config.TotalWeighting;
        for (int biomeIndex = 0; biomeIndex < Config.NumBiomes; ++biomeIndex)
        {
            int numEntries = Mathf.RoundToInt(numSeedPoints * Config.Biomes[biomeIndex].Weighting / totalBiomeWeighting);

            for (int entryIndex = 0; entryIndex < numEntries; ++entryIndex)
            {
                biomesToSpawn.Add((byte)biomeIndex);
            }
        }

        // spawn the individual biomes
        while (biomesToSpawn.Count > 0)
        {
            // pick a random seed point
            int seedPointIndex = Random.Range(0, biomesToSpawn.Count);

            // extract the biome index
            byte biomeIndex = biomesToSpawn[seedPointIndex];

            // remove seed point
            biomesToSpawn.RemoveAt(seedPointIndex);

            Perform_SpawnIndividualBiome(biomeIndex, mapResolution);
        }

        // save out the biome map
        Texture2D biomeMap = new Texture2D(mapResolution, mapResolution, TextureFormat.RGB24, false);
        for (int y = 0; y < mapResolution; ++y)
        {
            for (int x = 0; x < mapResolution; ++x)
            {
                float hue = ((float)WorkingData.BiomeMap_LowResolution[x, y] / (float)Config.NumBiomes);

                biomeMap.SetPixel(x, y, Color.HSVToRGB(hue, 0.75f, 0.75f));
            }
        }
        biomeMap.Apply();

        System.IO.File.WriteAllBytes("BiomeMap_LowResolution.png", biomeMap.EncodeToPNG());
    }

    Vector2Int[] NeighbourOffsets = new Vector2Int[] {
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 1),
        new Vector2Int(-1, -1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
    };

    /*
    Use Ooze based generation from here: https://www.procjam.com/tutorials/en/ooze/
    */
    void Perform_SpawnIndividualBiome(byte biomeIndex, int mapResolution)
    {
        // cache biome config
        BiomeConfigSO biomeConfig = Config.Biomes[biomeIndex].Biome;

        // pick spawn location
        Vector2Int spawnLocation = new Vector2Int(Random.Range(0, mapResolution), Random.Range(0, mapResolution));

        // pick the starting intensity
        float startIntensity = Random.Range(biomeConfig.MinIntensity, biomeConfig.MaxIntensity);

        // setup working list
        Queue<Vector2Int> workingList = new Queue<Vector2Int>();
        workingList.Enqueue(spawnLocation);

        // setup the visted map and target intensity map
        bool[,] visited = new bool[mapResolution, mapResolution];
        float[,] targetIntensity = new float[mapResolution, mapResolution];

        // set the starting intensity
        targetIntensity[spawnLocation.x, spawnLocation.y] = startIntensity;

        // let the oozing begin
        while (workingList.Count > 0)
        {
            Vector2Int workingLocation = workingList.Dequeue();

            // set the biome
            WorkingData.BiomeMap_LowResolution[workingLocation.x, workingLocation.y] = biomeIndex;
            visited[workingLocation.x, workingLocation.y] = true;
            WorkingData.BiomeStrengths_LowResolution[workingLocation.x, workingLocation.y] = targetIntensity[workingLocation.x, workingLocation.y];

            // traverse the neighbours
            for (int neighbourIndex = 0; neighbourIndex < NeighbourOffsets.Length; ++neighbourIndex)
            {
                Vector2Int neighbourLocation = workingLocation + NeighbourOffsets[neighbourIndex];

                // skip if invalid
                if (neighbourLocation.x < 0 || neighbourLocation.y < 0 || neighbourLocation.x >= mapResolution || neighbourLocation.y >= mapResolution)
                    continue;

                // skip if visited
                if (visited[neighbourLocation.x, neighbourLocation.y])
                    continue;

                // flag as visited
                visited[neighbourLocation.x, neighbourLocation.y] = true;

                // work out and store neighbour strength;
                float decayAmount = Random.Range(biomeConfig.MinDecayRate, biomeConfig.MaxDecayRate) * NeighbourOffsets[neighbourIndex].magnitude;
                float neighbourStrength = targetIntensity[workingLocation.x, workingLocation.y] - decayAmount;
                targetIntensity[neighbourLocation.x, neighbourLocation.y] = neighbourStrength;

                // if the strength is too low - stop
                if (neighbourStrength <= 0)
                {
                    continue;
                }

                workingList.Enqueue(neighbourLocation);
            }
        }
    }

    byte CalculateHighResBiomeIndex(int lowResMapSize, int lowResX, int lowResY, float fractionX, float fractionY)
    {
        float A = WorkingData.BiomeMap_LowResolution[lowResX,     lowResY];
        float B = (lowResX + 1) < lowResMapSize ? WorkingData.BiomeMap_LowResolution[lowResX + 1, lowResY] : A;
        float C = (lowResY + 1) < lowResMapSize ? WorkingData.BiomeMap_LowResolution[lowResX,     lowResY + 1] : A;
        float D = 0;

        if ((lowResX + 1) >= lowResMapSize)
            D = C;
        else if ((lowResY + 1) >= lowResMapSize)
            D = B;
        else
            D = WorkingData.BiomeMap_LowResolution[lowResX + 1, lowResY + 1];

        // perform bilinear filtering
        float filteredIndex = A * (1 - fractionX) * (1 - fractionY) + B * fractionX * (1 - fractionY) *
                              C * fractionY * (1 - fractionX) + D * fractionX * fractionY;

        // build an array of the possible biomes based on the values used to interpolate
        float[] candidateBiomes = new float[] {A, B, C, D};

        // find the neighbouring biome closest to the interpolated biome
        float bestBiome = -1f;
        float bestDelta = float.MaxValue;
        for (int biomeIndex = 0; biomeIndex < candidateBiomes.Length; ++biomeIndex)
        {
            float delta = Mathf.Abs(filteredIndex - candidateBiomes[biomeIndex]);

            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestBiome = candidateBiomes[biomeIndex];
            }
        }

        return (byte)Mathf.RoundToInt(bestBiome);
    }

    void Perform_BiomeGeneration_HighResolution(int lowResMapSize, int highResMapSize)
    {
        // allocate the biome map and strength map
        WorkingData.BiomeMap = new byte[highResMapSize, highResMapSize];
        WorkingData.BiomeStrengths = new float[highResMapSize, highResMapSize];

        // calculate map scale
        float mapScale = (float)lowResMapSize / (float)highResMapSize;

        // calculate the high res map
        for (int y = 0; y < highResMapSize; ++y)
        {
            int lowResY = Mathf.FloorToInt(y * mapScale);
            float yFraction = y * mapScale - lowResY;

            for(int x = 0; x < highResMapSize; ++x)
            {
                int lowResX = Mathf.FloorToInt(x * mapScale);
                float xFraction = x * mapScale - lowResX;

                WorkingData.BiomeMap[x, y] = CalculateHighResBiomeIndex(lowResMapSize, lowResX, lowResY, xFraction, yFraction);

                // this would do no interpolation - ie. point based
                //BiomeMap[x, y] = BiomeMap_LowResolution[lowResX, lowResY];
            }
        }

        // convert the biome map to a texture
        Texture2D tempTexture = new Texture2D(highResMapSize, highResMapSize, TextureFormat.RGB24, false);
        for (int y = 0; y < highResMapSize; ++y)
        {
            for (int x = 0; x < highResMapSize; ++x)
            {
                float hue = ((float)WorkingData.BiomeMap[x, y] / (float)Config.NumBiomes);

                tempTexture.SetPixel(x, y, Color.HSVToRGB(hue, 0.75f, 0.75f));
            }
        }
        tempTexture.Apply();

        string outputPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SceneManager.GetActiveScene().path), "ProcGen_BiomeMap.png");
        System.IO.File.Delete(outputPath);
        System.IO.File.WriteAllBytes(outputPath, tempTexture.EncodeToPNG());

        Debug.Log(outputPath);
        Output_BiomeMap = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    void Perform_HeightMapModification(int mapResolution, int alphaMapResolution)
    {
        float[,] heightMap = TargetTerrain.terrainData.GetHeights(0, 0, mapResolution, mapResolution);

        // execute any initial height modifiers
        if (Config.InitialHeightModifier != null)
        {
            BaseHeightMapModifier[] modifiers = Config.InitialHeightModifier.GetComponents<BaseHeightMapModifier>();

            foreach(var modifier in modifiers)
            {
                modifier.Execute(mapResolution, heightMap, TargetTerrain.terrainData.heightmapScale);
            }
        }

        // run heightmap generation for each biome
        for (int biomeIndex = 0; biomeIndex < Config.NumBiomes; ++biomeIndex)
        {
            var biome = Config.Biomes[biomeIndex].Biome;
            if (biome.HeightModifier == null)
                continue;

            BaseHeightMapModifier[] modifiers = biome.HeightModifier.GetComponents<BaseHeightMapModifier>();

            foreach(var modifier in modifiers)
            {
                modifier.Execute(mapResolution, heightMap, TargetTerrain.terrainData.heightmapScale, WorkingData.BiomeMap, biomeIndex, biome);
            }
        }

        // execute any post processing height modifiers
        if (Config.HeightPostProcessingModifier != null)
        {
            BaseHeightMapModifier[] modifiers = Config.HeightPostProcessingModifier.GetComponents<BaseHeightMapModifier>();
        
            foreach(var modifier in modifiers)
            {
                modifier.Execute(mapResolution, heightMap, TargetTerrain.terrainData.heightmapScale);
            }
        }     

        TargetTerrain.terrainData.SetHeights(0, 0, heightMap);

        // generate the slope map
        WorkingData.SlopeMap = new float[alphaMapResolution, alphaMapResolution];
        for (int y = 0; y < alphaMapResolution; ++y)
        {
            for (int x = 0; x < alphaMapResolution; ++x)
            {
                WorkingData.SlopeMap[x, y] = TargetTerrain.terrainData.GetInterpolatedNormal((float) x / alphaMapResolution, (float) y / alphaMapResolution).y;
            }
        }

        // convert the slope map to a texture
        Texture2D tempTexture = new Texture2D(alphaMapResolution, alphaMapResolution, TextureFormat.RGB24, false);
        for (int y = 0; y < alphaMapResolution; ++y)
        {
            for (int x = 0; x < alphaMapResolution; ++x)
            {
                float intensity = WorkingData.SlopeMap[x, y];

                tempTexture.SetPixel(x, y, new Color(intensity, intensity, intensity));
            }
        }
        tempTexture.Apply();

        string outputPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SceneManager.GetActiveScene().path), "ProcGen_SlopeMap.png");
        System.IO.File.Delete(outputPath);
        System.IO.File.WriteAllBytes(outputPath, tempTexture.EncodeToPNG());

        Output_SlopeMap = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    public int GetLayerForTexture(TextureConfig textureConfig)
    {
        return BiomeTextureToTerrainLayerIndex[textureConfig];
    }

    void Perform_TerrainPainting(int mapResolution, int alphaMapResolution)
    {
        float[,] heightMap = TargetTerrain.terrainData.GetHeights(0, 0, mapResolution, mapResolution);
        float[,,] alphaMaps = TargetTerrain.terrainData.GetAlphamaps(0, 0, alphaMapResolution, alphaMapResolution);

        // zero out all layers
        for (int y = 0; y < alphaMapResolution; ++y)
        {
            for (int x = 0; x < alphaMapResolution; ++x)
            {
                for (int layerIndex = 0; layerIndex < TargetTerrain.terrainData.alphamapLayers; ++layerIndex)
                {
                    alphaMaps[x, y, layerIndex] = 0;
                }
            }
        }   

        // run terrain painting for each biome
        for (int biomeIndex = 0; biomeIndex < Config.NumBiomes; ++biomeIndex)
        {
            var biome = Config.Biomes[biomeIndex].Biome;
            if (biome.TerrainPainter == null)
                continue;

            BaseTexturePainter[] modifiers = biome.TerrainPainter.GetComponents<BaseTexturePainter>();

            foreach(var modifier in modifiers)
            {
                modifier.Execute(this, mapResolution, heightMap, TargetTerrain.terrainData.heightmapScale, WorkingData.SlopeMap, alphaMaps, alphaMapResolution, WorkingData.BiomeMap, biomeIndex, biome);
            }
        }        

        // run texture post processing
        if (Config.PaintingPostProcessingModifier != null)
        {
            BaseTexturePainter[] modifiers = Config.PaintingPostProcessingModifier.GetComponents<BaseTexturePainter>();

            foreach(var modifier in modifiers)
            {
                modifier.Execute(this, mapResolution, heightMap, TargetTerrain.terrainData.heightmapScale, WorkingData.SlopeMap, alphaMaps, alphaMapResolution);
            }    
        }

        TargetTerrain.terrainData.SetAlphamaps(0, 0, alphaMaps);
    }

    void Perform_ObjectPlacement(int mapResolution, int alphaMapResolution)
    {
        float[,] heightMap = TargetTerrain.terrainData.GetHeights(0, 0, mapResolution, mapResolution);
        float[,,] alphaMaps = TargetTerrain.terrainData.GetAlphamaps(0, 0, alphaMapResolution, alphaMapResolution);

        // run object placement for each biome
        for (int biomeIndex = 0; biomeIndex < Config.NumBiomes; ++biomeIndex)
        {
            var biome = Config.Biomes[biomeIndex].Biome;
            if (biome.ObjectPlacer == null)
                continue;

            BaseObjectPlacer[] modifiers = biome.ObjectPlacer.GetComponents<BaseObjectPlacer>();

            foreach(var modifier in modifiers)
            {
                modifier.Execute(transform, mapResolution, heightMap, TargetTerrain.terrainData.heightmapScale, WorkingData.SlopeMap, alphaMaps, alphaMapResolution, WorkingData.BiomeMap, biomeIndex, biome);
            }
        }        
    }
#endif // UNITY_EDITOR
}
