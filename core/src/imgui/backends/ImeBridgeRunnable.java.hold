package com.andhook;

import android.content.Context;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.KeyEvent;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputConnectionWrapper;
import android.widget.EditText;

public final class ImeBridgeRunnable implements Runnable {
    private final long task;

    public ImeBridgeRunnable(long task) {
        this.task = task;
    }

    @Override
    public void run() {
        nativeRun(task);
    }

    private static native void nativeRun(long task);
}

final class ImeBridgeTextWatcher implements TextWatcher {
    @Override
    public void beforeTextChanged(CharSequence s, int start, int count, int after) {
    }

    @Override
    public void onTextChanged(CharSequence s, int start, int before, int count) {
    }

    @Override
    public void afterTextChanged(Editable s) {
        nativeTextChanged(s == null ? "" : s.toString());
    }

    private static native void nativeTextChanged(String text);
}

final class ImeBridgeEditText extends EditText {
    public ImeBridgeEditText(Context context) {
        super(context);
    }

    @Override
    public InputConnection onCreateInputConnection(EditorInfo outAttrs) {
        InputConnection base = super.onCreateInputConnection(outAttrs);
        return base == null ? null : new ImeBridgeInputConnection(base, this);
    }
}

final class ImeBridgeInputConnection extends InputConnectionWrapper {
    private final EditText editText;
    private int fallbackComposingStart = -1;
    private int fallbackComposingEnd = -1;

    ImeBridgeInputConnection(InputConnection target, EditText editText) {
        super(target, true);
        this.editText = editText;
    }

    @Override
    public boolean commitText(CharSequence text, int newCursorPosition) {
        boolean result = super.commitText(text, newCursorPosition);
        if (!result && text != null && text.length() > 0) {
            fallbackReplace(text, false);
        } else {
            fallbackComposingStart = -1;
            fallbackComposingEnd = -1;
            syncText();
        }
        return result;
    }

    @Override
    public boolean setComposingText(CharSequence text, int newCursorPosition) {
        boolean result = super.setComposingText(text, newCursorPosition);
        if (!result && text != null) {
            fallbackReplace(text, true);
        } else {
            syncText();
        }
        return result;
    }

    @Override
    public boolean finishComposingText() {
        fallbackComposingStart = -1;
        fallbackComposingEnd = -1;
        boolean result = super.finishComposingText();
        syncText();
        return result;
    }

    @Override
    public boolean deleteSurroundingText(int beforeLength, int afterLength) {
        return deleteAroundCursor(beforeLength <= 0 ? 1 : beforeLength, Math.max(0, afterLength));
    }

    @Override
    public boolean deleteSurroundingTextInCodePoints(int beforeLength, int afterLength) {
        return deleteAroundCursor(beforeLength <= 0 ? 1 : beforeLength, Math.max(0, afterLength));
    }

    @Override
    public boolean sendKeyEvent(KeyEvent event) {
        if (event != null
                && event.getAction() == KeyEvent.ACTION_DOWN
                && event.getKeyCode() == KeyEvent.KEYCODE_DEL) {
            return deleteAroundCursor(1, 0);
        }
        return super.sendKeyEvent(event);
    }

    private String currentText() {
        Editable editable = editText == null ? null : editText.getText();
        return editable == null ? "" : editable.toString();
    }

    private void syncText() {
        nativeTextChanged(currentText());
    }

    private void fallbackReplace(CharSequence text, boolean composing) {
        Editable editable = editText == null ? null : editText.getText();
        if (editable == null) {
            syncText();
            return;
        }

        int start;
        int end;
        if (composing && fallbackComposingStart >= 0 && fallbackComposingEnd >= fallbackComposingStart) {
            start = Math.min(fallbackComposingStart, editable.length());
            end = Math.min(fallbackComposingEnd, editable.length());
        } else {
            start = Math.max(0, Math.min(editText.getSelectionStart(), editText.getSelectionEnd()));
            end = Math.max(0, Math.max(editText.getSelectionStart(), editText.getSelectionEnd()));
        }

        CharSequence value = text == null ? "" : text;
        editable.replace(start, end, value);
        int cursor = start + value.length();
        editText.setSelection(Math.min(cursor, editable.length()));
        fallbackComposingStart = composing ? start : -1;
        fallbackComposingEnd = composing ? cursor : -1;
        syncText();
    }

    private boolean deleteAroundCursor(int beforeLength, int afterLength) {
        Editable editable = editText == null ? null : editText.getText();
        if (editable == null) {
            nativeDeleteBackward(Math.max(1, beforeLength));
            return true;
        }

        int selectionStart = editText.getSelectionStart();
        int selectionEnd = editText.getSelectionEnd();
        if (selectionStart < 0 || selectionEnd < 0) {
            selectionStart = selectionEnd = editable.length();
        }

        int start = Math.min(selectionStart, selectionEnd);
        int end = Math.max(selectionStart, selectionEnd);
        if (start == end) {
            start = moveByCodePoints(editable, start, -Math.max(1, beforeLength));
            end = moveByCodePoints(editable, end, Math.max(0, afterLength));
        }

        if (start < end) {
            editable.delete(start, end);
            editText.setSelection(Math.min(start, editable.length()));
        }

        fallbackComposingStart = -1;
        fallbackComposingEnd = -1;
        syncText();
        return true;
    }

    private static int moveByCodePoints(CharSequence text, int index, int count) {
        int value = Math.max(0, Math.min(index, text.length()));
        int remaining = Math.abs(count);
        int direction = count < 0 ? -1 : 1;
        while (remaining-- > 0) {
            if (direction < 0) {
                if (value <= 0) {
                    break;
                }
                value = Character.offsetByCodePoints(text, value, -1);
            } else {
                if (value >= text.length()) {
                    break;
                }
                value = Character.offsetByCodePoints(text, value, 1);
            }
        }
        return value;
    }

    private static native void nativeDeleteBackward(int count);
    private static native void nativeTextChanged(String text);
}
