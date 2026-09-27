using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class MaterialOffsetScroller : MonoBehaviour
{
    [Header("Скорость прокрутки (UV в секунду)")]
    public Vector2 scrollSpeed = new Vector2(0.5f, 0f);

    [Header("Настройки")]
    public int materialIndex = 0;

    [Tooltip("Имя текстуры Base Color в шейдере (например, _BaseMap)")]
    public string texturePropertyName = "_BaseMap";

    [Header("Опции")]
    public bool useUnscaledTime = false;
    public bool createMaterialInstance = true;

    private Material _material;
    private Vector2 _currentOffset;

    private void Awake()
    {
        var renderer = GetComponent<Renderer>();
        _material = createMaterialInstance
            ? renderer.materials[materialIndex]
            : renderer.sharedMaterials[materialIndex];

        if (!_material.HasProperty(texturePropertyName))
        {
            Debug.LogError($"[MaterialOffsetScroller] Свойство '{texturePropertyName}' не найдено. Запусти ShaderPropertyDumper чтобы узнать точное имя.", this);
            enabled = false;
            return;
        }

        _currentOffset = _material.GetTextureOffset(texturePropertyName);
    }

    private void Update()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        _currentOffset += scrollSpeed * dt;
        _currentOffset.x = Mathf.Repeat(_currentOffset.x, 1f);
        _currentOffset.y = Mathf.Repeat(_currentOffset.y, 1f);

        _material.SetTextureOffset(texturePropertyName, _currentOffset);
    }

    public void SetSpeed(Vector2 newSpeed) => scrollSpeed = newSpeed;
    public void SetSpeed(float x, float y) => scrollSpeed = new Vector2(x, y);

    public void ResetOffset()
    {
        _currentOffset = Vector2.zero;
        if (_material != null && _material.HasProperty(texturePropertyName))
            _material.SetTextureOffset(texturePropertyName, _currentOffset);
    }
}