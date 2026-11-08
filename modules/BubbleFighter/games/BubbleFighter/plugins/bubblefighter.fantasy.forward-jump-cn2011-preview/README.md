# Forward Jump CN2011 preview

Enable only for cn-279. This native preview enables the same effective capability byte that cn-279 sub_651960 sets for effects 100171/100224. cn-279 sub_7CC230 and sub_731060 retain native jump/roll eligibility and direction handling. For running state 301, the client rejects the request at sub_7D6980 because the legacy configuration has no bubbleJumpEnable value; while W+Space is held, the preview temporarily changes only the two-byte branch at RVA 0x7D69E4 and restores it immediately when the request or running state ends. The function's remaining state, effect, resource and cooldown checks remain active. No input samples, equipment lists or network packets are replaced. Changes are process-local and reset with the game.

CN2012 sub_51C0C0 checks a corresponding protected capability and additional statuses 21/22. This preview does not grant those statuses. The source of the newer default remains under investigation. Test W+Space and running W+Space, while confirming lateral/backward rolls, standing jump and blocked actions retain their normal behavior. Disable the old declarative Forward Jump package. The successful Auto Dash preview may remain enabled.

This remains an unverified local gameplay candidate. The guarded research DLL enables the capability only for actor states 101/102/103 and uses the running gate only for state 301; state 104 (roll) is never granted the jump path. It is not published or enabled by default. The DLL is x86 to match the game. Inspect fthook.log for native capability activation.

For this diagnostic build, `fthook.log` also records each changed action state
and the W/Space input flags. The records are transition-only and are intended
to identify additional client states without changing them.
