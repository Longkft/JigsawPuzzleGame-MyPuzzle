using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class Tile
{
    public enum Direction { UP, DOWN, LEFT, RIGHT }
    public enum PosNegType { POS, NEG, NONE }

    // [MỚI] Padding không fix cứng là 20 nữa, sẽ tính toán lại khi SetTileSize
    public static int padding = 20;
    // [MỚI] tileSize không fix cứng là 100 nữa.
    public static int tileSize = 100;

    // [MỚI] Hằng số để tham chiếu. Giả sử template khớp nối được vẽ cho size chuẩn là 100.
    private const int REF_TILE_SIZE = 100;
    // [MỚI] Tỷ lệ scale hiện tại của khớp nối.
    private static float currentCurveScale = 1.0f;

    private Dictionary<(Direction, PosNegType), LineRenderer> mLineRenderers = new Dictionary<(Direction, PosNegType), LineRenderer>();

    // [MỚI] Không khởi tạo ngay. Sẽ khởi tạo lại mỗi khi đổi kích thước.
    public static List<Vector2> BezCurve = new List<Vector2>();

    private Texture2D mOriginalTexture;
    public Texture2D finalCut { get; private set; }
    public static readonly Color TransparentColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);

    private PosNegType[] mCurveTypes = new PosNegType[4]
    {
        PosNegType.NONE, PosNegType.NONE, PosNegType.NONE, PosNegType.NONE,
    };

    private bool[,] mVisited;
    private Stack<Vector2Int> mStack = new Stack<Vector2Int>();

    public int xIndex = 0;
    public int yIndex = 0;

    public static TilesSorting tilesSorting = new TilesSorting();

    // =================================================================================
    // [MỚI] HÀM QUAN TRỌNG NHẤT: Gọi hàm này để thiết lập kích thước mới cho Tile
    // =================================================================================
    public static void SetNewTileSize(int newSize)
    {
        tileSize = newSize;

        // Tính toán lại padding theo tỷ lệ (ví dụ 20% của kích thước tile)
        // Đảm bảo tối thiểu là 2 pixel để tránh lỗi.
        padding = Mathf.Max(2, Mathf.RoundToInt(newSize * 0.2f));

        // Tính tỷ lệ scale so với kích thước chuẩn 100.
        // Ví dụ: size mới là 50 -> scale là 0.5. Size mới là 200 -> scale là 2.0.
        currentCurveScale = (float)newSize / REF_TILE_SIZE;

        // Tạo lại đường cong mẫu với tỷ lệ mới
        RegenerateBezierTemplate();
    }

    // [MỚI] Hàm tạo lại mẫu đường cong đã được scale
    private static void RegenerateBezierTemplate()
    {
        BezCurve.Clear();
        // Lấy điểm gốc từ template (Giả sử template thiết kế cho size 100)
        List<Vector2> originalPoints = BezierCurve.PointList2(TemplateBezierCurve.templateControlPoints, 0.001f);

        foreach (var p in originalPoints)
        {
            // Nhân tọa độ với tỷ lệ scale để phóng to/thu nhỏ khớp nối
            BezCurve.Add(p * currentCurveScale);
        }
    }
    // =================================================================================
    public void SetCurveType(Direction dir, PosNegType type)
  {
    mCurveTypes[(int)dir] = type;
  }

  public PosNegType GetCurveType(Direction dir)
  {
    return mCurveTypes[(int)dir];
  }

  public Tile(Texture2D texture)
  {
    mOriginalTexture = texture;
    //int padding = mOffset.x;
    int tileSizeWithPadding = 2 * padding + tileSize;

        // [FIX] Thêm kiểm tra để đảm bảo texture không quá nhỏ
        if (tileSizeWithPadding <= 0) tileSizeWithPadding = 1;

        finalCut = new Texture2D(tileSizeWithPadding, tileSizeWithPadding, TextureFormat.ARGB32, false);

    // We initialise this newly created texture with transparent color.
    for (int i = 0; i < tileSizeWithPadding; ++i)
    {
      for (int j = 0; j < tileSizeWithPadding; ++j)
      {
        finalCut.SetPixel(i, j, TransparentColor);
      }
    }
  }

  public void Apply()
  {
    FloodFillInit();
    FloodFill();
    finalCut.Apply();
  }

    void FloodFillInit()
    {
        int tileSizeWithPadding = 2 * padding + tileSize;
        mVisited = new bool[tileSizeWithPadding, tileSizeWithPadding];

        // (Giữ nguyên vòng lặp khởi tạo mVisited...)
        for (int i = 0; i < tileSizeWithPadding; ++i)
            for (int j = 0; j < tileSizeWithPadding; ++j)
                mVisited[i, j] = false;

        List<Vector2> pts = new List<Vector2>();
        for (int i = 0; i < mCurveTypes.Length; ++i)
        {
            pts.AddRange(CreateCurve((Direction)i, mCurveTypes[i]));
        }

        for (int i = 0; i < pts.Count; ++i)
        {
            // [FIX CỰC KỲ QUAN TRỌNG]: Kẹp giá trị (Clamp) để tránh lỗi IndexOutOfRange 
            // khi tính toán số thực (float) chuyển sang số nguyên (int) có thể bị lệch 1 pixel.
            int px = Mathf.Clamp((int)pts[i].x, 0, tileSizeWithPadding - 1);
            int py = Mathf.Clamp((int)pts[i].y, 0, tileSizeWithPadding - 1);
            mVisited[px, py] = true;
        }

        Vector2Int start = new Vector2Int(tileSizeWithPadding / 2, tileSizeWithPadding / 2);
        mVisited[start.x, start.y] = true;
        mStack.Push(start);
    }

    void Fill(int x, int y)
    {
        // Cần đảm bảo tọa độ lấy mẫu không vượt quá ảnh gốc
        int sampleX = Mathf.Clamp(x + xIndex * tileSize, 0, mOriginalTexture.width - 1);
        int sampleY = Mathf.Clamp(y + yIndex * tileSize, 0, mOriginalTexture.height - 1);

        Color c = mOriginalTexture.GetPixel(sampleX, sampleY);
        c.a = 1.0f;
        finalCut.SetPixel(x, y, c);
    }

    // [TỐI ƯU] Viết gọn lại hàm FloodFill
    void FloodFill()
    {
        int width_height = padding * 2 + tileSize;
        while (mStack.Count > 0)
        {
            Vector2Int v = mStack.Pop();
            Fill(v.x, v.y);

            CheckNeighbor(v.x + 1, v.y, width_height); // Phải
            CheckNeighbor(v.x - 1, v.y, width_height); // Trái
            CheckNeighbor(v.x, v.y + 1, width_height); // Trên
            CheckNeighbor(v.x, v.y - 1, width_height); // Dưới
        }
    }

    // Hàm phụ trợ cho FloodFill
    void CheckNeighbor(int x, int y, int limit)
    {
        if (x >= 0 && x < limit && y >= 0 && y < limit && !mVisited[x, y])
        {
            mVisited[x, y] = true;
            mStack.Push(new Vector2Int(x, y));
        }
    }

    public static LineRenderer CreateLineRenderer(UnityEngine.Color color, float lineWidth = 1.0f)
  {
    GameObject obj = new GameObject();
    LineRenderer lr = obj.AddComponent<LineRenderer>();

    lr.startColor = color;
    lr.endColor = color;
    lr.startWidth = lineWidth;
    lr.endWidth = lineWidth;
    lr.material = new Material(Shader.Find("Sprites/Default"));
    return lr;
  }

  public static void TranslatePoints(List<Vector2> iList, Vector2 offset)
  {
    for (int i = 0; i < iList.Count; i++)
    {
      iList[i] += offset;
    }
  }

  public static void InvertY(List<Vector2> iList)
  {
    for (int i = 0; i < iList.Count; i++)
    {
      iList[i] = new Vector2(iList[i].x, -iList[i].y);
    }
  }

  public static void SwapXY(List<Vector2> iList)
  {
    for (int i = 0; i < iList.Count; ++i)
    {
      iList[i] = new Vector2(iList[i].y, iList[i].x);
    }
  }

    public List<Vector2> CreateCurve(Direction dir, PosNegType type)
    {
        int padding_x = padding;
        int padding_y = padding;
        // [MỚI] Sử dụng tileSize động thay vì số cứng
        int sw = tileSize;
        int sh = tileSize;

        // Sử dụng BezCurve đã được scale ở hàm SetNewTileSize
        List<Vector2> pts = new List<Vector2>(BezCurve);
        switch (dir)
        {
            case Direction.UP:
                if (type == PosNegType.POS) { TranslatePoints(pts, new Vector2(padding_x, padding_y + sh)); }
                else if (type == PosNegType.NEG) { InvertY(pts); TranslatePoints(pts, new Vector2(padding_x, padding_y + sh)); }
                else
                {
                    pts.Clear();
                    // [MỚI] Sửa vòng lặp: chạy theo tileSize thay vì 100
                    for (int i = 0; i < tileSize; ++i) pts.Add(new Vector2(i + padding_x, padding_y + sh));
                }
                break;
            case Direction.RIGHT:
                if (type == PosNegType.POS) { SwapXY(pts); TranslatePoints(pts, new Vector2(padding_x + sw, padding_y)); }
                else if (type == PosNegType.NEG) { InvertY(pts); SwapXY(pts); TranslatePoints(pts, new Vector2(padding_x + sw, padding_y)); }
                else
                {
                    pts.Clear();
                    // [MỚI] Sửa vòng lặp
                    for (int i = 0; i < tileSize; ++i) pts.Add(new Vector2(padding_x + sw, i + padding_y));
                }
                break;
            case Direction.DOWN:
                if (type == PosNegType.POS) { InvertY(pts); TranslatePoints(pts, new Vector2(padding_x, padding_y)); }
                else if (type == PosNegType.NEG) { TranslatePoints(pts, new Vector2(padding_x, padding_y)); }
                else
                {
                    pts.Clear();
                    // [MỚI] Sửa vòng lặp
                    for (int i = 0; i < tileSize; ++i) pts.Add(new Vector2(i + padding_x, padding_y));
                }
                break;
            case Direction.LEFT:
                if (type == PosNegType.POS) { InvertY(pts); SwapXY(pts); TranslatePoints(pts, new Vector2(padding_x, padding_y)); }
                else if (type == PosNegType.NEG) { SwapXY(pts); TranslatePoints(pts, new Vector2(padding_x, padding_y)); }
                else
                {
                    pts.Clear();
                    // [MỚI] Sửa vòng lặp
                    for (int i = 0; i < tileSize; ++i) pts.Add(new Vector2(padding_x, i + padding_y));
                }
                break;
        }
        return pts;
    }

    public void DrawCurve(Direction dir, PosNegType type, UnityEngine.Color color)
  {
    if (!mLineRenderers.ContainsKey((dir, type)))
    {
      mLineRenderers.Add((dir, type), CreateLineRenderer(color));
    }

    LineRenderer lr = mLineRenderers[(dir, type)];
    lr.gameObject.SetActive(true);
    lr.startColor = color;
    lr.endColor = color;
    lr.gameObject.name = "LineRenderer_" + dir.ToString() + "_" + type.ToString();
    List<Vector2> pts = CreateCurve(dir, type);

    lr.positionCount = pts.Count;
    for (int i = 0; i < pts.Count; ++i)
    {
      lr.SetPosition(i, pts[i]);
    }
  }

  public void HideAllCurves()
  {
    foreach (var item in mLineRenderers)
    {
      item.Value.gameObject.SetActive(false);
    }
  }

  public void DestroyAllCurves()
  {
    foreach (var item in mLineRenderers)
    {
      GameObject.Destroy(item.Value.gameObject);
    }

    mLineRenderers.Clear();
  }

}
