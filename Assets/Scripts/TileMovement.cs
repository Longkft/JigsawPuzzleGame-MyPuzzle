using UnityEngine;
using UnityEngine.EventSystems; // Bắt buộc cho Unity 6 Input System

public class TileMovement : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public Tile tile { get; set; }
    private Vector3 mOffset = Vector3.zero;
    private SpriteRenderer mSpriteRenderer;
    private Camera mMainCamera; // Cache camera để tối ưu hiệu năng cho Unity 6

    public delegate void DelegateOnTileInPlace(TileMovement tm);
    public DelegateOnTileInPlace onTileInPlace;

    void Start()
    {
        mSpriteRenderer = GetComponent<SpriteRenderer>();
        mMainCamera = Camera.main; // Lấy Camera 1 lần lúc đầu
    }

    private Vector3 GetCorrectPosition()
    {
        // SỬA: Dùng Tile.tileSize thay vì 100f để khớp với logic sinh map ở BoardGen
        return new Vector3(tile.xIndex * Tile.tileSize, tile.yIndex * Tile.tileSize, 0f);
    }

    // --- XỬ LÝ CLICK & KÉO THẢ TRONG UNITY 6 ---

    // Thay thế OnMouseDown
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!GameApp.Instance.TileMovementEnabled) return;

        // Chuyển tọa độ màn hình sang World
        Vector3 worldPoint = mMainCamera.ScreenToWorldPoint(eventData.position);
        worldPoint.z = 0;

        mOffset = transform.position - worldPoint;

        // Đưa mảnh ghép lên trên cùng
        if (Tile.tilesSorting != null) // Check null cho an toàn
            Tile.tilesSorting.BringToTop(mSpriteRenderer);
    }

    // Thay thế OnMouseDrag
    public void OnDrag(PointerEventData eventData)
    {
        if (!GameApp.Instance.TileMovementEnabled) return;

        Vector3 worldPoint = mMainCamera.ScreenToWorldPoint(eventData.position);
        worldPoint.z = 0;

        transform.position = worldPoint + mOffset;
    }

    // Thay thế OnMouseUp
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!GameApp.Instance.TileMovementEnabled) return;

        // Kiểm tra khoảng cách
        float dist = (transform.position - GetCorrectPosition()).magnitude;

        // [FIX QUAN TRỌNG] Thay số cứng 20.0f bằng tỷ lệ theo kích thước tile
        // Ví dụ: Chỉ hút khi khoảng cách nhỏ hơn 50% kích thước mảnh ghép
        float snapDistance = Tile.tileSize * 0.5f;

        if (dist < snapDistance)
        {
            transform.position = GetCorrectPosition();
            onTileInPlace?.Invoke(this);

            // Đảm bảo Z luôn = 0 khi đã đặt đúng vị trí để không bị ẩn
            Vector3 pos = transform.position;
            pos.z = 0;
            transform.position = pos;
        }
    }
}