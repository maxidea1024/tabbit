package dev.tabbit.clover;

import android.os.Bundle;
import android.view.View;

import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;
import androidx.core.view.WindowInsetsControllerCompat;

import com.getcapacitor.BridgeActivity;

/**
 * 창 하나. 게임은 그 안의 WebView 가 그대로 돕니다.
 *
 * 여기서 하는 일은 시스템 바를 감추는 것뿐입니다. 판이 화면을 꽉 채우므로 위의 상태 표시줄은
 * 점수 칸 위에 겹치고, 아래의 제스처 바는 「낸다」·「버린다」 버튼과 같은 자리에 있어
 * 누르려던 것이 뒤로 가기가 됩니다.
 *
 * 감춘 바는 화면 가장자리를 쓸면 잠깐 돌아오고 다시 감춰집니다.
 */
public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        hideSystemBars();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        // 다른 앱에 다녀오면 바가 돌아와 있습니다. 돌아올 때마다 다시 감춥니다.
        if (hasFocus) hideSystemBars();
    }

    private void hideSystemBars() {
        View decor = getWindow().getDecorView();
        WindowCompat.setDecorFitsSystemWindows(getWindow(), false);

        WindowInsetsControllerCompat controller =
            WindowCompat.getInsetsController(getWindow(), decor);
        controller.setSystemBarsBehavior(
            WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
        controller.hide(WindowInsetsCompat.Type.systemBars());
    }
}
