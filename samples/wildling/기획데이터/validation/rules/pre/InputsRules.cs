using Tabbit.Validation;

// `pre` 규칙은 워크북을 열기 전에 돕니다. 볼 수 있는 것은 모델이 아니라 recipe가 넘긴 설정이고,
// 입력에 대한 이 프로젝트의 규약이 그래서 여기 옵니다.

internal static class InputsRules
{
    public static void Validate(IPreContext context)
    {
        context.Info($"검증을 시작합니다. Locale={context.Option("Locale", "없음")}.");

        // 코어는 이 키를 모릅니다. 두 글자 코드라는 것은 이 프로젝트의 규약이고, 그 판단이
        // 여기 있는 것이 자유 키/값 주머니가 존재하는 이유입니다.
        if (context.Option("Locale", "KR").Length != 2)
            context.Error("`Locale` 옵션은 두 글자 코드여야 합니다.");
    }
}
