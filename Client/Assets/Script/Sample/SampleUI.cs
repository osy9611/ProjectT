using ProjectT.UGUI;
using UnityEngine.UI;

namespace ProjectT.Sample
{
    // 하나의 스크립트를 System·Dynamic·Static 프리팹에 함께 쓰며, 분류는 프리팹의 UIBase.Type으로 정한다.
    public class SampleUI : UIBase
    {
        private enum eText
        {
            Title
        }

        private enum eButton
        {
            CloseButton,
            NextButton
        }

        private int showCount;

        protected override void OnEnter()
        {
            Bind<Text>(typeof(eText));
            Bind<Button>(typeof(eButton));
            GetButton((int)eButton.CloseButton).onClick.AddListener(() => Hide());
            GetButton((int)eButton.NextButton).onClick.AddListener(OpenNextInternal);
            FeatureSampleRunner.Write($"{name} OnEnter ({Type})");
        }

        protected override void OnShow()
        {
            showCount++;
            GetText((int)eText.Title).text = $"{name}\n{Type} / Show {showCount}";
            FeatureSampleRunner.Write($"{name} OnShow");
        }

        protected override void OnHide()
        {
            FeatureSampleRunner.Write($"{name} OnHide");
        }

        protected override void OnLeave()
        {
            FeatureSampleRunner.Write($"{name} OnLeave");
        }

        // Static 스택 위에 다른 Static을 올려 이전 UI 숨김과 닫을 때 복원을 확인한다.
        private void OpenNextInternal()
        {
            Global.UI.CreateWidget<SampleUI>(UIDefine.eUIType.SampleStaticB).Show();
        }
    }
}
