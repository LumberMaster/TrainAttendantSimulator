using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public class DialogChoiceButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;

        public Button Button => button;
        public TMP_Text Label => label;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (label == null) label = GetComponentInChildren<TMP_Text>();
        }
    }
}