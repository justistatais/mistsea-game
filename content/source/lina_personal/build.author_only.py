import json
from pathlib import Path
R=Path(__file__).resolve().parent
V='LINA-WORK-20261010-v1'
rows=[
('LI01-APPENDIX','CQ-LINA-01','A transfer outside the accident file',
'''Transfer appendix LT-17 records the movement of Lina's younger brother after the harbour accident. The receiving code was added to a separate family-transfer register. It was not part of the incident file that the technician compared for the tower investigation. The date belongs to the transfer, not to the day on which this copy was made.

The line shows that a receiving office acknowledged the transfer. It does not say that the person still lives at that office, or that a current address has been verified. A blank current-status field must remain blank. The index can help a custodian forward a question without displaying a private address. Lina permits the technician to compare this appendix with its index for this inquiry. She has not permitted either sheet to be placed on a public noticeboard.''',
'''转移附页LT-17记录莉娜弟弟在港口事故后的一次转移，接收代码后来补在单独的家人转移登记册中。它不属于技术员调查塔站事故时比较的原事故档案。这里的日期指转移事件，不是抄写日期。

接收机关的确认只证明当次接收，不证明他现在仍住在那里，也不证明当前地址已查实。当前状态的空项应保留空白。保管人可通过索引转交问题而不展示私人住址。莉娜许可技术员为这次查询比较附页与索引，未许可将两页放上公共看板。'''),
('LI01-INDEX','CQ-LINA-01','Index I-LT-17',
'''Index I-LT-17 refers to transfer appendix LT-17. The old register and this appendix belong to different filing groups; their row numbers are not evidence that one person made two journeys. Compare the transfer code and the event date before treating them as the same record.

The forwarding field is a protected service code. It can be used by the designated custodian to ask whether the recipient wants a letter. It is not a present home address. The current condition and the preferred form of contact have not yet been established. You may keep a controlled copy for Lina or continue the limited inquiry. Both choices preserve the original page and neither authorises an uninvited visit.''',
'''索引I-LT-17指向附页LT-17。旧名册与附页分属不同归档组，行号不同不表示同一人经历了两次转移。将两页对应之前，需核对转移代码和事件日期。

转交栏是受保护的服务代码，仅指定保管人可用来询问收件人是否愿意收到信件，它不是当前住址。当前状况与愿意怎样联系仍未确定。可为莉娜保留受控副本，也可继续限域查询；两条路径都保留原件，均不授权未经邀请的来访。'''),
('LI02-RELAY','CQ-LINA-02','A sealed inquiry through Joan',
'''Joan's forwarding desk can accept one sealed inquiry under code LT-17. The letter should ask what form of contact the recipient currently prefers. Address it to the protected recipient code, not to a guessed household. Include a return route through this desk. Do not promise that Lina or the technician will visit.

Lina asks the technician to help with this particular letter. The technician's own draft may be saved and revised locally before it is sent. Saving, checking the meaning and handing the letter to the desk are separate acts. Once the current saved version is dispatched, retain its hash and route. A later rewrite is not the same letter. The custodian can give an explicit delivery or return acknowledgement; a forwarding permission alone is not that acknowledgement.''',
'''琼的转交台可按LT-17代码接收一封封缄询问信。信中应询问收件人当前希望怎样联系，写受保护的收件代码，不猜测家庭住址；回信走这张台登记的回路，不承诺莉娜或技术员来访。

莉娜请求协助的是这封具体的信。寄出前可在本地保存和修改自己的草稿。保存、确认意思和实际交到台上是三件事。当前保存版本送出后，保留其哈希与路径，后来的改稿不是已经寄出的那封信。保管人可给明确送达或退回记录，中继许可本身不是送达记录。'''),
('LI02-DIRECT','CQ-LINA-02','The recipient permits a sealed direct letter',
'''The recipient of LT-17 has authorised a sealed letter addressed to that service code for the present inquiry. The permission covers correspondence only. The local forwarding desk keeps the return channel; the private destination is not copied into the technician's draft or the public archive.

This option changes the letter's receiving path, not its agreed purpose. Send only the current version that Lina has asked the technician to prepare. Direct correspondence does not bypass the need for a receiving acknowledgement. It also does not permit a visit, a public announcement or disclosure to other companions. If you prefer the custodian route, the original protected service remains available.''',
'''LT-17的收件人允许为本次询问接收写该服务代码的封缄直接书信，许可仅覆盖通信。本地转交台保留回路，不把私人目的地抄进技术员草稿或公共档案。

这条路径改变收件方式，不改变约定用途。只发送莉娜请技术员准备的当前版本。直接书信仍需实际接收确认，也不许可来访、公告或向其他同伴披露。若选择保管人中继，原受保护服务仍可使用。'''),
('LI02-DELIVERY','CQ-LINA-02','Acknowledgement of the same sealed letter',
'''The forwarding desk acknowledges receipt of the one letter actually dispatched under LT-17. The attached local record identifies its sealed instance, the dispatch path and the saved body fingerprint. It does not contain a public address or a promise to meet.

This acknowledgement is supplied only after the technician has physically handed the agreed letter into the service. It confirms the bounded receiving step. It does not turn a rewritten local draft into another delivery and it does not confirm the recipient's current living situation. Keep it with the original dispatch record. A personal reply, when obtained at a later legitimate communication point, must be checked in its own scope.''',
'''转交台确认接收了按LT-17实际寄出的那一封信。附随本地记录标明封缄实例、送出路径和保存正文的指纹，不包含公开地址或见面承诺。

技术员将约定的信真正交进服务后才提供这张回执。它确认有限的接收步骤，不把本地改稿当成另一封已送信，也不确认收件人当前居住状况。请与原发送记录一起保管。后来在合法通信端取得的本人回信，仍须按自己的范围单独核验。'''),
('LI03-REPLY','CQ-LINA-03','A reply in his own words',
'''I received the letter sent under LT-17. The receiving record and the forwarding code match the sealed inquiry. I am Lina's brother, and I now have a life in the border settlements. Please give this reply to her through the agreed private channel. My address is not part of the message for the technician's public report.

I am willing to exchange letters now. I have not agreed to a visit in this reply. We do not have to explain all the years in one conversation. If Lina would like to ask about meeting, let that be a separate question and wait for a separate answer. Continuing to write or taking a pause are acceptable. Please do not turn a small permission into a promise for either of us.''',
'''我收到按LT-17寄出的信了，接收记录与转交代码对应这封封缄询问信。我是莉娜的弟弟，现在在边地聚落有自己的生活。请通过约定私人回路把这份答复交给她，住址不属于技术员公共报告的内容。

我现在愿意通信，这封回信中尚未同意来访。我们不必在一次谈话里解释所有这些年。如果莉娜想问见面的可能，请作为另一个问题提出，等另一个答复。继续写信或暂缓都可以，请不要把有限许可改成任何一方的承诺。'''),
('LI03-MEETING','CQ-LINA-03','A separate answer about this meeting',
'''This is the answer to the separate question about meeting, not a reinterpretation of the earlier letter. Lina says she would like to meet if her brother also wants that arrangement. Her brother agrees to the same bounded arrangement at the public communication point, with a time set through the protected service. Neither reply publishes a home address.

Record each person's current agreement separately. If either person pauses or withdraws that agreement before the arrangement is confirmed, keep the correspondence route and ask again. Permission to exchange letters cannot fill a missing meeting consent. Confirming this arrangement does not claim that a physical reunion has already taken place; the present work is to settle a contact form both people accept.''',
'''这是对单独提出的见面问题的答复，不是重解早先的信。莉娜愿意在弟弟也希望这项安排时见面；弟弟同意在公共通信点、由受保护服务约定时间的同一有限安排。两份答复都不公开住址。

需分别记录两人的当前同意。安排确认前任何一方暂缓或撤回时，保留通信路线并重新询问，不能用通信许可补缺失的见面同意。确认安排不声称身体上的团聚已经发生；当前工作是确定双方都接受的联系形式。'''),
('LI03-TOPICS','CQ-LINA-03','Lina sets the limit for an ordinary conversation',
'''Lina has read the private reply. She is willing to talk with the technician about folding a sheet neatly, without quoting its contents. That permission is only for the ordinary folding topic. The words, address and personal details in the reply remain private.

She is also willing to discuss the general habit of keeping a person's original wording with the technician and Mira. It is a separate, limited topic; it is not permission to show Mira the reply. Ask for the particular topic and respect a later withdrawal. A shipboard invitation or membership in the party does not disclose a private letter to everyone on board.''',
'''莉娜读过私人回信了。她愿意和技术员谈怎样把纸折整齐，不引用信中内容；该许可只覆盖普通折纸话题。信中的原话、地址和私人细节仍受保护。

她也愿意和技术员、米拉讨论保留本人原话这一一般习惯，这是另一项有限话题，不是将回信交给米拉的许可。请就具体话题询问，并尊重后来的撤回。船上的邀约或入队身份不等于向所有人披露私人信件。''')]
materials=[dict(id=i,node=n,version=V,english=en,chinese=zh) for i,n,title,en,zh in rows]
(R/'learner_package.json').write_text(json.dumps(dict(version=V,review_status='original_unreviewed_human_pending',materials=materials),ensure_ascii=False,indent=2)+'\n',encoding='utf8')
