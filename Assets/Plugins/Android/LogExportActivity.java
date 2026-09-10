package com.zhongermate.chunithmmobile;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

public final class LogExportActivity extends Activity {
    private static final int CREATE_LOG = 7841;
    private static final String UNITY_OBJECT = "CHUNITHM Mobile Runtime";
    private static final String EXTRA_SOURCE = "source";
    private String sourcePath;

    public static void exportFile(String sourcePath) {
        final Activity unity = UnityPlayer.currentActivity;
        if (unity == null || sourcePath == null || sourcePath.trim().isEmpty()) return;
        unity.runOnUiThread(() -> {
            try {
                Intent proxy = new Intent(unity, LogExportActivity.class);
                proxy.putExtra(EXTRA_SOURCE, sourcePath);
                unity.startActivity(proxy);
            } catch (Exception exception) {
                Log.e("CHUNITHM_LOG", "Unable to open log exporter", exception);
                sendResult("无法打开系统保存窗口：" + exception.getMessage());
            }
        });
    }

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        sourcePath = getIntent().getStringExtra(EXTRA_SOURCE);
        if (sourcePath == null || !new File(sourcePath).isFile()) {
            sendResult("待导出的日志文件不存在。");
            finish();
            return;
        }
        if (state != null) return;
        Intent picker = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        picker.addCategory(Intent.CATEGORY_OPENABLE);
        picker.setType("text/plain");
        picker.putExtra(Intent.EXTRA_TITLE, "chunithm-mobile-logs-" +
            new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.US).format(new Date()) + ".txt");
        try {
            startActivityForResult(picker, CREATE_LOG);
        } catch (Exception exception) {
            Log.e("CHUNITHM_LOG", "No document creator is available", exception);
            sendResult("设备没有可用的系统文件保存窗口。");
            finish();
        }
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != CREATE_LOG) return;
        if (resultCode != RESULT_OK || data == null || data.getData() == null) {
            sendResult("日志导出已取消。");
            finish();
            return;
        }
        final Uri destination = data.getData();
        new Thread(() -> copyAndFinish(destination), "CHUNITHM Log Export").start();
    }

    private void copyAndFinish(Uri destination) {
        try (FileInputStream input = new FileInputStream(sourcePath);
             OutputStream output = getContentResolver().openOutputStream(destination, "w")) {
            if (output == null) throw new IOException("无法写入选择的位置");
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            output.flush();
            sendResult("OK|日志文件已保存。");
        } catch (Exception exception) {
            Log.e("CHUNITHM_LOG", "Log export failed", exception);
            sendResult("日志保存失败：" + exception.getMessage());
        }
        runOnUiThread(this::finish);
    }

    private static void sendResult(String message) {
        UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnLogExported", message == null ? "" : message);
    }
}
