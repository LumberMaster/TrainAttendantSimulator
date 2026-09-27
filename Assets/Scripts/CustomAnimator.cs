using UnityEngine;

public class CustomAnimator : MonoBehaviour
{
    [Header("Вращение")]
    [Tooltip("Скорость вращения вокруг оси Y (градусы/сек)")]
    [SerializeField] private float rotationSpeed = 180f;

    [Header("Перемещение (парение вверх-вниз)")]
    [Tooltip("Амплитуда движения по оси Y")]
    [SerializeField] private float moveAmplitude = 0.25f;

    [Tooltip("Скорость перемещения")]
    [SerializeField] private float moveSpeed = 2f;

    [Tooltip("Использовать локальные оси (иначе мировые)")]
    [SerializeField] private bool useLocalSpace = true;

    private Vector3 _startLocalPosition;
    private float _time;

    private void Awake()
    {
        // Запоминаем ЛОКАЛЬНУЮ позицию — относительно родителя
        _startLocalPosition = transform.localPosition;
    }

    private void OnEnable()
    {
        // При повторном включении скрипта сбрасываем фазу,
        // чтобы монетка не "прыгала"
        _time = 0f;
        transform.localPosition = _startLocalPosition;
    }

    private void FixedUpdate()
    {
        // Всё движение привязано к FixedUpdate, чтобы работало
        // синхронно с физикой и не зависело от FPS
        float dt = Time.fixedDeltaTime;

        // --- Вращение ---
        transform.Rotate(
            Vector3.up,
            rotationSpeed * dt,
            useLocalSpace ? Space.Self : Space.World
        );

        // --- Перемещение вверх-вниз ---
        _time += dt * moveSpeed;
        float offsetY = Mathf.Sin(_time) * moveAmplitude;

        // Меняем ЛОКАЛЬНУЮ позицию — объект будет следовать за родителем
        // и при этом парить вверх-вниз относительно него
        transform.localPosition = _startLocalPosition + Vector3.up * offsetY;
    }

    // Метод для отладки — вызывается в редакторе при смене значений
    private void OnValidate()
    {
        if (!Application.isPlaying)
            _startLocalPosition = transform.localPosition;
    }
}