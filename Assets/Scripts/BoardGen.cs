using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardGen : MonoBehaviour
{
    private string imageFilename;
    Sprite mBaseSpriteOpaque;
    Sprite mBaseSpriteTransparent;

    GameObject mGameObjectOpaque;
    GameObject mGameObjectTransparent;

    public float ghostTransparency = 0.1f;

    // Jigsaw tiles creation.
    public int numTileX { get; private set; }
    public int numTileY { get; private set; }

    Tile[,] mTiles = null;
    GameObject[,] mTileGameObjects = null;

    public Transform parentForTiles = null;

    // Access to the menu.
    public Menu menu = null;
    private List<Rect> regions = new List<Rect>();
    private List<Coroutine> activeCoroutines = new List<Coroutine>();

    // [MỚI] BIẾN QUAN TRỌNG: Số lượng cột mảnh ghép bạn muốn chia theo chiều ngang.
    // Ví dụ: Ảnh gốc chia ra 4x3. Muốn gấp đôi (8x6) thì điền số 8 vào đây trong Inspector.
    [Header("Jigsaw Settings")]
    [Tooltip("Số lượng mảnh ghép mong muốn theo chiều ngang.")]
    public int targetColumns = 4;

    Sprite LoadBaseTexture()
    {
        Texture2D originalTex = SpriteUtils.LoadTexture(imageFilename);
        if (!originalTex.isReadable)
        {
            Debug.Log("Error: Texture is not readable");
            return null;
        }

        // 1. TÍNH TOÁN KÍCH THƯỚC CHUẨN
        int safeTargetColumns = Mathf.Max(1, targetColumns);
        int newTileSize = originalTex.width / safeTargetColumns;

        // Cập nhật Tile
        Tile.SetNewTileSize(newTileSize);

        // 2. TÍNH TOÁN KÍCH THƯỚC MỚI ĐÃ CẮT GỌT (TRIM)
        // Đảm bảo chiều rộng và cao chia hết cho tileSize
        int trimmedWidth = (originalTex.width / newTileSize) * newTileSize;
        int trimmedHeight = (originalTex.height / newTileSize) * newTileSize;

        // 3. TẠO TEXTURE ĐÃ CẮT GỌT (Để khớp 100% với lưới Grid)
        // Lấy phần pixel từ góc dưới trái (0,0) lên
        Color[] pixels = originalTex.GetPixels(0, 0, trimmedWidth, trimmedHeight);

        Texture2D trimmedTex = new Texture2D(trimmedWidth, trimmedHeight);
        trimmedTex.SetPixels(pixels);
        trimmedTex.Apply();

        // Debug xem cắt bớt bao nhiêu
        Debug.Log($"Gốc: {originalTex.width}x{originalTex.height} -> Cắt còn: {trimmedWidth}x{trimmedHeight} (TileSize: {newTileSize})");


        // 4. THÊM PADDING VÀO TEXTURE ĐÃ CẮT (Logic cũ của bạn)
        Texture2D finalTex = new Texture2D(
            trimmedWidth + Tile.padding * 2,
            trimmedHeight + Tile.padding * 2,
            TextureFormat.ARGB32,
            false);

        // Fill màu trắng/trong suốt
        for (int x = 0; x < finalTex.width; ++x)
            for (int y = 0; y < finalTex.height; ++y)
                finalTex.SetPixel(x, y, Color.white); // Hoặc Color.clear nếu muốn nền trong suốt

        // Copy pixel từ ảnh đã cắt vào giữa
        for (int x = 0; x < trimmedWidth; ++x)
        {
            for (int y = 0; y < trimmedHeight; ++y)
            {
                Color color = trimmedTex.GetPixel(x, y);
                color.a = 1.0f;
                finalTex.SetPixel(x + Tile.padding, y + Tile.padding, color);
            }
        }
        finalTex.Apply();

        Sprite sprite = SpriteUtils.CreateSpriteFromTexture2D(
            finalTex,
            0,
            0,
            finalTex.width,
            finalTex.height);

        return sprite;
    }

    // Start is called before the first frame update
    // Start is called before the first frame update
    void Start()
    {
        imageFilename = GameApp.Instance.GetJigsawImageName();

        // Load texture và tính toán lại kích thước Tile TRƯỚC KHI làm gì khác
        mBaseSpriteOpaque = LoadBaseTexture();

        mGameObjectOpaque = new GameObject();
        mGameObjectOpaque.name = imageFilename + "_Opaque";
        mGameObjectOpaque.AddComponent<SpriteRenderer>().sprite = mBaseSpriteOpaque;
        mGameObjectOpaque.GetComponent<SpriteRenderer>().sortingLayerName = "Opaque";

        // Tạo ảnh mờ đằng sau
        mBaseSpriteTransparent = CreateTransparentView(mBaseSpriteOpaque.texture);
        mGameObjectTransparent = new GameObject();
        mGameObjectTransparent.name = imageFilename + "_Transparent";
        mGameObjectTransparent.AddComponent<SpriteRenderer>().sprite = mBaseSpriteTransparent;
        mGameObjectTransparent.GetComponent<SpriteRenderer>().sortingLayerName = "Transparent";

        mGameObjectOpaque.gameObject.SetActive(false);

        SetCameraPosition();

        // Create the Jigsaw tiles.
        StartCoroutine(Coroutine_CreateJigsawTiles());
    }

    Sprite CreateTransparentView(Texture2D tex)
    {
        Texture2D newTex = new Texture2D(
          tex.width,
          tex.height,
          TextureFormat.ARGB32,
          false);

        for (int x = 0; x < newTex.width; x++)
        {
            for (int y = 0; y < newTex.height; y++)
            {
                Color c = tex.GetPixel(x, y);
                // [LƯU Ý] Sử dụng Tile.padding mới
                if (x > Tile.padding &&
                    x < (newTex.width - Tile.padding) &&
                    y > Tile.padding &&
                    y < (newTex.height - Tile.padding))
                {
                    c.a = ghostTransparency;
                }
                newTex.SetPixel(x, y, c);
            }
        }
        newTex.Apply();

        Sprite sprite = SpriteUtils.CreateSpriteFromTexture2D(
          newTex,
          0,
          0,
          newTex.width,
          newTex.height);
        return sprite;
    }

    void SetCameraPosition()
    {
        Camera.main.transform.position = new Vector3(mBaseSpriteOpaque.texture.width / 2,
          mBaseSpriteOpaque.texture.height / 2, -10.0f);
        int smaller_value = Mathf.Min(mBaseSpriteOpaque.texture.width, mBaseSpriteOpaque.texture.height);
        Camera.main.orthographicSize = smaller_value * 0.8f;
    }

    public static GameObject CreateGameObjectFromTile(Tile tile)
    {
        GameObject obj = new GameObject();

        obj.name = "TileGameObe_" + tile.xIndex.ToString() + "_" + tile.yIndex.ToString();

        // [LƯU Ý] Sử dụng Tile.tileSize mới để đặt vị trí
        obj.transform.position = new Vector3(tile.xIndex * Tile.tileSize, tile.yIndex * Tile.tileSize, 0.0f);

        SpriteRenderer spriteRenderer = obj.AddComponent<SpriteRenderer>();

        // [LƯU Ý] Sử dụng Tile.padding và Tile.tileSize mới để cắt sprite
        spriteRenderer.sprite = SpriteUtils.CreateSpriteFromTexture2D(
          tile.finalCut,
          0,
          0,
          Tile.padding * 2 + Tile.tileSize,
          Tile.padding * 2 + Tile.tileSize);

        BoxCollider2D box = obj.AddComponent<BoxCollider2D>();

        // 1. Kích thước vùng click: Chỉ bằng kích thước tile chính (không tính padding khớp nối)
        // Để khi các mảnh ghép sát nhau, click không bị dính sang mảnh bên cạnh
        box.size = new Vector2(Tile.tileSize, Tile.tileSize);

        // 2. Dịch chuyển tâm vùng click:
        // Vì Sprite có pivot ở (0,0), nên ta phải dịch Collider ra giữa.
        // Offset = Độ dày viền (Padding) + Nửa kích thước Tile
        float centerOffset = Tile.padding + (Tile.tileSize / 2.0f);

        box.offset = new Vector2(centerOffset, centerOffset);


        TileMovement tileMovement = obj.AddComponent<TileMovement>();
        tileMovement.tile = tile;

        return obj;
    }

    void CreateJigsawTiles()
  {
    Texture2D baseTexture = mBaseSpriteOpaque.texture;
    numTileX = baseTexture.width / Tile.tileSize;
    numTileY = baseTexture.height / Tile.tileSize;

    mTiles = new Tile[numTileX, numTileY];
    mTileGameObjects = new GameObject[numTileX, numTileY];

    for(int i = 0; i < numTileX; i++)
    {
      for(int j = 0; j < numTileY; j++)
      {
        mTiles[i, j] = CreateTile(i, j, baseTexture);
        mTileGameObjects[i, j] = CreateGameObjectFromTile(mTiles[i, j]);
        if(parentForTiles != null)
        {
          mTileGameObjects[i, j].transform.SetParent(parentForTiles);
        }
      }
    }

    // Enable the bottom panel and set the onlcick delegate to the play button.
    menu.SetEnableBottomPanel(true);
    menu.btnPlayOnClick = ShuffleTiles;
  }

    IEnumerator Coroutine_CreateJigsawTiles()
    {
        // [QUAN TRỌNG] Tính toán lại số lượng tile dựa trên kích thước texture đã padding và tileSize mới
        Texture2D baseTexture = mBaseSpriteOpaque.texture;
        // Chiều rộng thực tế của phần ảnh (trừ padding)
        int contentWidth = baseTexture.width - (Tile.padding * 2);
        int contentHeight = baseTexture.height - (Tile.padding * 2);

        numTileX = contentWidth / Tile.tileSize;
        numTileY = contentHeight / Tile.tileSize;

        Debug.Log($"Tạo lưới Jigsaw: {numTileX} x {numTileY} mảnh.");

        mTiles = new Tile[numTileX, numTileY];
        mTileGameObjects = new GameObject[numTileX, numTileY];

        for (int i = 0; i < numTileX; i++)
        {
            for (int j = 0; j < numTileY; j++)
            {
                mTiles[i, j] = CreateTile(i, j, baseTexture);
                mTileGameObjects[i, j] = CreateGameObjectFromTile(mTiles[i, j]);
                if (parentForTiles != null)
                {
                    mTileGameObjects[i, j].transform.SetParent(parentForTiles);
                }

                yield return null;
            }
        }

        // Enable the bottom panel and set the delegate to button play on click.
        menu.SetEnableBottomPanel(true);
        menu.btnPlayOnClick = ShuffleTiles;
    }

    Tile CreateTile(int i, int j, Texture2D baseTexture)
  {
    Tile tile = new Tile(baseTexture);
    tile.xIndex = i;
    tile.yIndex = j;

    // Left side tiles.
    if (i == 0)
    {
      tile.SetCurveType(Tile.Direction.LEFT, Tile.PosNegType.NONE);
    }
    else
    {
      // We have to create a tile that has LEFT direction opposite curve type.
      Tile leftTile = mTiles[i - 1, j];
      Tile.PosNegType rightOp = leftTile.GetCurveType(Tile.Direction.RIGHT);
      tile.SetCurveType(Tile.Direction.LEFT, rightOp == Tile.PosNegType.NEG ?
        Tile.PosNegType.POS : Tile.PosNegType.NEG);
    }

    // Bottom side tiles
    if (j == 0)
    {
      tile.SetCurveType(Tile.Direction.DOWN, Tile.PosNegType.NONE);
    }
    else
    {
      Tile downTile = mTiles[i, j - 1];
      Tile.PosNegType upOp = downTile.GetCurveType(Tile.Direction.UP);
      tile.SetCurveType(Tile.Direction.DOWN, upOp == Tile.PosNegType.NEG ?
        Tile.PosNegType.POS : Tile.PosNegType.NEG);
    }

    // Right side tiles.
    if (i == numTileX - 1)
    {
      tile.SetCurveType(Tile.Direction.RIGHT, Tile.PosNegType.NONE);
    }
    else
    {
      float toss = UnityEngine.Random.Range(0f, 1f);
      if(toss < 0.5f)
      {
        tile.SetCurveType(Tile.Direction.RIGHT, Tile.PosNegType.POS);
      }
      else
      {
        tile.SetCurveType(Tile.Direction.RIGHT, Tile.PosNegType.NEG);
      }
    }

    // Up side tile.
    if(j == numTileY - 1)
    {
      tile.SetCurveType(Tile.Direction.UP, Tile.PosNegType.NONE);
    }
    else
    {
      float toss = UnityEngine.Random.Range(0f, 1f);
      if (toss < 0.5f)
      {
        tile.SetCurveType(Tile.Direction.UP, Tile.PosNegType.POS);
      }
      else
      {
        tile.SetCurveType(Tile.Direction.UP, Tile.PosNegType.NEG);
      }
    }

    tile.Apply();
    return tile;
  }


  // Update is called once per frame
  void Update()
  {

  }

  #region Shuffling related codes

  private IEnumerator Coroutine_MoveOverSeconds(GameObject objectToMove, Vector3 end, float seconds)
  {
    float elaspedTime = 0.0f;
    Vector3 startingPosition = objectToMove.transform.position;
    while(elaspedTime < seconds)
    {
      objectToMove.transform.position = Vector3.Lerp(
        startingPosition, end, (elaspedTime / seconds));
      elaspedTime += Time.deltaTime;

      yield return new WaitForEndOfFrame();
    }
    objectToMove.transform.position = end;
  }

  void Shuffle(GameObject obj)
  {
    if(regions.Count == 0)
    {
      regions.Add(new Rect(-300.0f, -100.0f, 50.0f, numTileY * Tile.tileSize));
      regions.Add(new Rect((numTileX+1) * Tile.tileSize, -100.0f, 50.0f, numTileY * Tile.tileSize));
    }

    int regionIndex = UnityEngine.Random.Range(0, regions.Count);
    float x = UnityEngine.Random.Range(regions[regionIndex].xMin, regions[regionIndex].xMax);
    float y = UnityEngine.Random.Range(regions[regionIndex].yMin, regions[regionIndex].yMax);

    Vector3 pos = new Vector3(x, y, 0.0f);
    Coroutine moveCoroutine = StartCoroutine(Coroutine_MoveOverSeconds(obj, pos, 1.0f));
    activeCoroutines.Add(moveCoroutine);
  }

  IEnumerator Coroutine_Shuffle()
  {
    for(int i = 0; i < numTileX; ++i)
    {
      for(int j = 0; j < numTileY; ++j)
      {
        Shuffle(mTileGameObjects[i, j]);
        yield return null;
      }
    }

    foreach(var item in activeCoroutines)
    {
      if(item != null)
      {
        yield return null;
      }
    }

    OnFinishedShuffling();
  }

  public void ShuffleTiles()
  {
    StartCoroutine(Coroutine_Shuffle());
  }

    // Tìm hàm này trong BoardGen.cs và thay thế toàn bộ nội dung của nó
    void OnFinishedShuffling()
    {
        activeCoroutines.Clear();

        menu.SetEnableBottomPanel(false);
        StartCoroutine(Coroutine_CallAfterDelay(() => menu.SetEnableTopPanel(true), 1.0f));
        GameApp.Instance.TileMovementEnabled = true;

        StartTimer();

        for (int i = 0; i < numTileX; ++i)
        {
            for (int j = 0; j < numTileY; ++j)
            {
                GameObject tileObj = mTileGameObjects[i, j];
                TileMovement tm = tileObj.GetComponent<TileMovement>();
                tm.onTileInPlace += OnTileInPlace;

                SpriteRenderer spriteRenderer = tileObj.GetComponent<SpriteRenderer>();

                // [FIX QUAN TRỌNG TẠI ĐÂY] 
                // KHÔNG GỌI Tile.tilesSorting.BringToTop(spriteRenderer); NỮA!

                // Thay vào đó, hãy đặt lại Order về 0 (để nó nằm trên Background -10)
                if (spriteRenderer)
                {
                    spriteRenderer.sortingOrder = 0;
                }

                // [FIX THÊM] Đảm bảo chắc chắn vị trí Z = 0 sau khi bay xong
                Vector3 pos = tileObj.transform.position;
                pos.z = 0f;
                tileObj.transform.position = pos;
            }
        }

        menu.SetTotalTiles(numTileX * numTileY);

        // Reset lại bộ đếm sorting nếu class TilesSorting của bạn có biến đếm
        // Tile.tilesSorting.Reset(); (Nếu có hàm này)
    }

    IEnumerator Coroutine_CallAfterDelay(System.Action function, float delay)
  {
    yield return new WaitForSeconds(delay);
    function();
  }


  public void StartTimer()
  {
    StartCoroutine(Coroutine_Timer());
  }

  IEnumerator Coroutine_Timer()
  {
    while(true)
    {
      yield return new WaitForSeconds(1.0f);
      GameApp.Instance.SecondsSinceStart += 1;

      menu.SetTimeInSeconds(GameApp.Instance.SecondsSinceStart);
    }
  }

  public void StopTimer()
  {
    StopCoroutine(Coroutine_Timer());
  }

  #endregion

  public void ShowOpaqueImage()
  {
    mGameObjectOpaque.SetActive(true);
  }

  public void HideOpaqueImage()
  {
    mGameObjectOpaque.SetActive(false);
  }

  void OnTileInPlace(TileMovement tm)
  {
    GameApp.Instance.TotalTilesInCorrectPosition += 1;

    tm.enabled = false;
    Destroy(tm);

    SpriteRenderer spriteRenderer = tm.gameObject.GetComponent<SpriteRenderer>();
    Tile.tilesSorting.Remove(spriteRenderer);

    if (GameApp.Instance.TotalTilesInCorrectPosition == mTileGameObjects.Length)
    {
      //Debug.Log("Game completed. We will implement an end screen later");
      menu.SetEnableTopPanel(false);
      menu.SetEnableGameCompletionPanel(true);

      // Reset the values.
      GameApp.Instance.SecondsSinceStart = 0;
      GameApp.Instance.TotalTilesInCorrectPosition = 0;
    }
    menu.SetTilesInPlace(GameApp.Instance.TotalTilesInCorrectPosition);
  }
}
