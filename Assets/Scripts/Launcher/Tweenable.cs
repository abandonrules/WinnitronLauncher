﻿using UnityEngine;
using System.Collections;

public class Tweenable : MonoBehaviour {

    GoTween currentTween;

    public void TweenPosition(Vector3 position)
    {
        if (currentTween != null) StopTween();

        currentTween = Go.to(transform, GM.options.tweenTime, new GoTweenConfig()
            .position(position)
            .setEaseType(GoEaseType.ExpoOut));

        currentTween.setOnCompleteHandler(c => { onMoveComplete(); });
    }

    public void TweenLocalPosition(Vector3 position)
    {
        if (currentTween != null) StopTween();

        currentTween = Go.to(transform, GM.options.tweenTime, new GoTweenConfig()
            .localPosition(position)
            .setEaseType(GoEaseType.ExpoOut));

        currentTween.setOnCompleteHandler(c => { onMoveComplete(); });
    }

    public void Tween(Vector3 position, Vector3 scale)
    {
        Tween(position, scale, GM.options.tweenTime);
    }

    public void Tween(Vector3 position, Vector3 scale, float time)
    {
        if (currentTween != null) StopTween();

        currentTween = Go.to(transform, time, new GoTweenConfig()
            .localPosition(position)
            .scale(scale)
            .setEaseType(GoEaseType.ExpoOut));

        currentTween.setOnCompleteHandler(c => { onMoveComplete(); });
    }

    private void onMoveComplete()
    {
        //ResetTempTransform();
    }

    public void StopTween()
    {
        currentTween.destroy();
    }
}