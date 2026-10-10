"""Original candidate scripts; never copy this author tool into a player export."""
from pathlib import Path
import json, hashlib, re
ROOT=Path(__file__).resolve().parents[3]
OUT=Path(__file__).parent
seeds=json.loads((ROOT/'content/source/part09.json').read_text(encoding='utf-8'))['banter_seeds']
scripts={}
def add(key,rows,consequence,gate='public',progress=None):
    scripts[key]=dict(rows=rows,consequence=consequence,gate=gate,progress=progress)
# Speakers HERO and companion persistent IDs are the actual participants; no unseen narrator is added.
add('BAN-LINA-1',[
('CP-LINA','The corner kept lifting whenever I turned the chart. Hold it flat for a moment, please.','我一转海图，角就翘起来。请先帮我压平。'),
('HERO','The weight is heavy enough. Is the board bent?','压块够重。是板子弯了吗？'),
('CP-LINA','Only at this end. I put a thin strip underneath instead of forcing the whole board down.','只有这一端。我在下面垫了窄条，没有硬把整块板子压下去。'),
('HERO','Now it stays still. Shall I move the strip into the middle?','现在稳了。要把窄条移到中间吗？'),
('CP-LINA','Leave it here. In the middle it would lift the good side. Sometimes a small repair has a very particular address.','留在这儿。放在中间反而会把平的那边顶起来。有时小修补也得放准地方。'),
('HERO','I will mark the underside, then. Someone can find it next time the board comes apart.','那我在底面做个记号，下次拆开还能找到。'),
('CP-LINA','Good. And we can finally look at the chart without having to argue with its corners.','好。总算能看海图，不必先和它的角较劲。')], 'ordinary_chart_board_kept_flat')
add('BAN-LINA-2',[
('CP-LINA','The fold is beginning to tear. I keep opening the same edge first.','折痕快破了。我每次都先从同一边打开。'),
('HERO','Would a new envelope help? You can keep the writing covered while we try it.','换个封套会有用吗？试的时候可以把字盖住。'),
('CP-LINA','Yes. You have heard the part I chose to share. The rest can stay inside.','会。你听过我愿意分享的那部分，其余的留在里面就好。'),
('HERO','Then I will measure the folded paper, not spread the letter out.','那我量折好后的纸，不把信摊开。'),
('CP-LINA','Give it a little room at the top. I do not want to squeeze it into a neat shape and damage it.','上面留一点余量。我不想为了折得整齐把它挤坏。'),
('HERO','This flap closes without another crease. Shall I leave the envelope blank?','这个封口不用再折一道。封套留空白吗？'),
('CP-LINA','Please. I know what is in it. It does not need to tell everyone who passes the table.','留空白吧。我知道里面是什么，不必告诉每个经过桌边的人。')], 'private_letter_envelope_closed_no_text_copied','lina_reply_folding')
add('BAN-LINA-3',[
('CP-LINA','Watch the loose end of that ribbon. It tells us more than the flag pressed against the mast.','看那段松开的带子。它比贴在桅杆上的旗更好判断。'),
('HERO','It points towards the stern. Does that settle the wind direction?','它指向船尾。这就能确定风向了吗？'),
('CP-LINA','For this little patch of deck. The cabin can bend the air around us. Take another reading where it is clear.','只能说明甲板这一小块。船舱会让气流绕过来。再到开阔处看一次。'),
('HERO','The second ribbon moves at a slightly different angle.','第二条的角度稍有不同。'),
('CP-LINA','Then keep both observations. I would rather write a small uncertainty than draw one very confident arrow.','那就都记下。我宁愿留一点不确定，也不想画一根过分肯定的箭头。'),
('HERO','And for sailing we still need the current chart and the watch report.','真正开船时还得看当前海图和值守记录。'),
('CP-LINA','Exactly. Today we are learning to look, while the ship is safely tied up.','是的。今天船还好好系在泊位上，我们只是练习观察。')], 'two_local_wind_observations_not_route_permission')
add('BAN-OREN-1',[
('CP-OREN','That narrow space is for the old spanner. I made it fit the handle instead of the other way round.','那个窄格放旧扳手。我让格子适合柄，没有反过来磨柄。'),
('HERO','It is a rather plain frame for something you spent so long repairing.','你修了这么久，框倒挺朴素。'),
('CP-OREN','It is supposed to make the tools easy to find. If people have to admire it first, I have made a bad cupboard.','它是让人方便找工具的。要是得先欣赏一番，那我柜子做坏了。'),
('HERO','There is still an empty space. Are you saving it for a new tool?','还空了一格。留给新工具吗？'),
('CP-OREN','For the tool that is being used. An empty place can remind me to ask where it went.','留给正在用的工具。空位能提醒我问问它去了哪里。'),
('HERO','I will put the spanner back with its handle facing out.','那我把扳手放回去，柄朝外。'),
('CP-OREN','Thank you. That is the whole ceremony. No polishing required.','谢谢。仪式到这里就完了，不用抛光。')], 'own_repaired_toolframe_used','public','CQ-OREN-02')
add('BAN-OREN-2',[
('HERO','You said the stool needed one screw. Why are there three pieces beside your plate?','你说凳子只缺一颗螺钉，怎么盘子旁边有三块零件？'),
('CP-OREN','Because I said that before I looked underneath. The joint was split, and the brace was loose.','因为那是我看底面之前说的。接头裂了，撑条也松了。'),
('HERO','So the first estimate was wrong.','所以最初估计错了。'),
('CP-OREN','Yes. I should have called it an estimate instead of promising a quick job. Put your bowl on the bench for now.','是。我该说是估计，而不是答应马上修好。碗暂时放长凳上吧。'),
('HERO','Can the repair wait until we finish eating?','可以吃完饭再修吗？'),
('CP-OREN','It can, if nobody sits on it. I will leave a note where someone would actually see it.','可以，只要没人坐。我把便条放到人真能看到的地方。'),
('HERO','Then put the pieces away. Your soup will not wait as patiently as the stool.','那先收好零件。汤可没凳子这么有耐心。')], 'stool_marked_out_of_use_until_repair')
add('BAN-OREN-3',[
('CP-OREN','The cup itself is sound. The handle came away because this old join was doing all the work.','杯身没问题。旧接头独自吃力，杯把才掉下来。'),
('HERO','Can we just glue the handle back on?','把杯把粘回去就行吗？'),
('CP-OREN','We can mend it, but I would test it with cold water before trusting it with hot tea. Hold the cup, not the handle.','能修，但用它喝热茶之前，我会先装冷水试。拿杯身，别拿把。'),
('HERO','This old crack still shows.','旧裂痕还看得见。'),
('CP-OREN','That is all right. A visible repair reminds us to treat it carefully. I do not need to make the cup look new.','没关系。看得见的修补提醒我们小心用，不必把杯子装成新的。'),
('HERO','I will leave it upside down here to dry.','我把它倒扣在这里晾干。'),
('CP-OREN','And I will write which cup it is. Otherwise we will have a very careful warning attached to the wrong cup.','我记清是哪只杯。不然一张十分谨慎的警告，会贴到另一只杯上。')], 'ordinary_cup_repair_drying_no_key')
add('BAN-MIRA-1',[
('CP-MIRA','The book is on the lower shelf, but its card sends everyone upstairs.','书在下层，书目卡却把人指到楼上。'),
('HERO','Shall I throw the card away and write a clean one?','把卡扔掉，写张干净的吗？'),
('CP-MIRA','Turn it over. We can date the correction and leave the old location visible. It explains why someone might still look there.','翻过来吧。我们写上更正日期，旧位置也留着。这样还去那里找的人有个解释。'),
('HERO','There is room for another change underneath.','下面还能再记一次变动。'),
('CP-MIRA','Good. Public shelves move about. A card that allows correction is more useful than a very solemn permanent label.','好。公共书架常换位置。能更正的卡，比一本正经的永久标签更有用。'),
('HERO','Lower shelf, left side. I have also left a blank line for whoever moves it next.','下层左侧。我也给下一个移书的人留了空行。'),
('CP-MIRA','Now let us put the card where readers start looking, instead of keeping it in my notebook.','现在把卡放到读者开始找书的地方，别夹在我的笔记里。')], 'public_catalogue_card_corrected_old_entry_retained')
add('BAN-MIRA-2',[
('HERO','You have not written a single note in that book.','这本书你一条注释也没写。'),
('CP-MIRA','I wanted to find out what happened to the traveller before I started asking about the road.','我想先知道旅人后来怎样，再追问路是怎么回事。'),
('HERO','Was the ending convincing?','结尾可信吗？'),
('CP-MIRA','For a story, yes. I liked the part where she went home and found her neighbour had watered the plants.','作为故事，我觉得可信。我喜欢她回家后发现邻居一直替她浇花的那段。'),
('HERO','No missing document, no final explanation?','没有缺失的文件，也没有最后的说明？'),
('CP-MIRA','Just a living plant. I do enjoy a book that lets a small kindness remain small.','只有一盆活着的植物。我也喜欢一本书，让小小的善意就保持那么小。'),
('HERO','Then I will borrow it when you have finished. I promise not to demand footnotes from the flowers.','你读完借我吧。我保证不向花索要脚注。')], 'ordinary_story_left_unannotated')
add('BAN-MIRA-3',[
('CP-MIRA','This used envelope has a name written across the front. The back is still blank.','这个旧封套正面写着姓名，背面还空着。'),
('HERO','Could we cover the name and use it for public shelf cards?','盖住名字，拿来放公共书目卡吗？'),
('CP-MIRA','Only after the original contents have gone back to their proper custodian. An empty cover is not permission to move its papers.','得先把原来的文件交回合适的保管人。封套空了，也不等于允许搬走里面的纸。'),
('HERO','This one is already marked as empty stock. I can turn it inside out.','这一只已标为废空套。我可以把它翻面。'),
('CP-MIRA','Then label the new use and the date. Do not put the old name on the new label just because it fits.','那就写新用途和日期。别因为旧名字放得下，就抄到新标签上。'),
('HERO','Public book cards, spare sleeves. That should be enough.','公共书目卡，备用封套。这样就够了。'),
('CP-MIRA','Enough, and much easier to find. A label should help with the task in front of us.','够了，也好找。标签应该帮助我们完成眼前的事。')], 'empty_stock_sleeve_label_contains_no_identity')
add('BAN-TESS-1',[
('CP-TESS','That little line ends at the edge because the map ends there. The path does not.','小线到边缘就断了，是地图到头了，路没有。'),
('HERO','Do people use it often?','常有人走吗？'),
('CP-TESS','On ordinary dry mornings. It leads from the washing place to the houses above it. A loaded cart takes another way.','普通的晴天早晨有人走。从洗衣处上到房子那边。载重车走另一条。'),
('HERO','So adding one line would still leave out quite a lot.','看来只补一根线，还会漏掉不少东西。'),
('CP-TESS','Write who uses it and when, then leave space for a local correction. I have walked it, but that does not make me every neighbour.','写上谁用、何时用，再留地方让当地人更正。我走过，但我不代表每个邻居。'),
('HERO','I will mark it as an ordinary footpath, not a verified route for our ship.','我标成日常步道，不当作我们船的已核航线。'),
('CP-TESS','Good. People can recognise their morning walk without being promised something much larger.','好。人们能认出自己的晨路，不必顺带收到更大的承诺。')], 'everyday_path_margin_note_not_travel_gate')
add('BAN-TESS-2',[
('CP-TESS','The next watch may not know which corner stays wet after a shower.','下一班可能不知道下过雨后哪个角落一直湿。'),
('HERO','I can leave them a weather warning.','我可以留个天气警告。'),
('CP-TESS','Leave an observation with it. Say when we looked and where. A warning without those things is hard to use tomorrow.','也写上观察。说明何时看、看哪里。没这些，警告到了明天就很难用。'),
('HERO','After the last shower, beside the forward rail. Check the surface before carrying anything across.','上一阵雨后，前栏杆旁。搬东西经过前先检查地面。'),
('CP-TESS','That is useful. Keep the forecast separate; we are not giving the next sailor a new weather instrument.','有用。预报另写；我们又没给下一个船员一台新天气仪。'),
('HERO','And I will attach it at eye level, not underneath the bucket.','我把它挂到看得到的高度，不压在桶底下。'),
('CP-TESS','Thank you. Whoever comes next should not need to know our habits to find the note.','谢谢。后来的人不该先熟悉我们的习惯，才能找到便条。')], 'dated_local_weather_note_left')
add('BAN-TESS-3',[
('HERO','This knot is tight enough to hold the whole harbour. Who tied it?','这绳结紧得像能系住整个港口。谁打的？'),
('CP-TESS','I do not know. Let us ask before we give someone the credit or the blame.','我不知道。给人记功或怪人之前，先问问。'),
('HERO','Can I cut the rope?','我能把绳子剪开吗？'),
('CP-TESS','This spare coil carries no load, so we can work on the knot. Leave the actual mooring lines alone.','这是不吃力的备用绳圈，可以慢慢解。实际系泊绳别碰。'),
('HERO','There. The end comes through if I loosen this turn first.','好了。先松这一圈，绳头就能穿过去。'),
('CP-TESS','Now coil it with a little space. The next person may have cold hands and much less patience than us.','现在卷松一点。下个人可能手冷，耐心也没我们多。'),
('HERO','We have solved the knot, at least. The mystery of its maker can wait.','至少结解开了。打结的人是谁，可以以后再说。')], 'unloaded_spare_coil_loosened_author_unknown')
add('BAN-LINA-OREN-2',[
('CP-LINA','This compass needle keeps sticking against the glass. I would not use it to set a course.','这指南针的针一直蹭着玻璃，我不会拿它定航向。'),
('CP-OREN','The case is bent. I can inspect it without asking you to trust the reading.','壳子弯了。我可以检查，不用让你先信它的读数。'),
('CP-LINA','Good. I have heard people say that if one compass is bad, every route drawn with a compass must be false.','好。有人说一个指南针坏了，所有用指南针画的航线就都是假的。'),
('CP-OREN','That skips several questions. Which instrument, which day, and what else did the navigator check?','这跳过了好几个问题。哪台仪器、哪一天，领航的人还核对过什么？'),
('CP-LINA','We will mark this one as out of use. Existing routes keep their own sources and need their own checks.','我们给这一台标停用。已有航线保留自己的来源，也要各自核查。'),
('CP-OREN','And I will put the cover beside it, rather than making it look ready on the shelf.','我把拆下的盖子放旁边，免得摆在架上像能用了。'),
('CP-LINA','That is a warning I can understand from the doorway.','这个警告，我在门口就能看懂。')], 'faulty_ordinary_compass_labelled_no_route_invalidated')
add('BAN-LINA-MIRA-1',[
('CP-LINA','I need this note near the reef, but it covers the depth figures.','这条备注该放礁石旁，可它挡住了水深数字。'),
('CP-MIRA','Could you put a small reference mark here and keep the full sentence in the margin?','这里放个小标记，完整句子写在页边好吗？'),
('CP-LINA','The margin already has two versions. I do not want the newest one to look like it was always there.','页边已经有两版。我不想让新版看起来像一直都在。'),
('CP-MIRA','Date it, then. Keep the earlier note readable and say which observation led to the change.','那写日期。旧备注留得能读，也说明什么观察让你改了它。'),
('CP-LINA','That takes more room than an arrow.','这比一根箭头占地方。'),
('CP-MIRA','It does. We can use a separate card instead of making the chart too crowded to read.','是。我们用另页卡，别把海图挤得看不清。'),
('CP-LINA','A dated card and a small mark, then. The rocks can keep the space they need.','那就带日期的卡，加小标记。给礁石留够地方。')], 'dated_chart_margin_reference_added')
add('BAN-LINA-MIRA-2',[
('CP-LINA','I want to keep the words as they came. They sound different when I turn them into a summary.','我想保留收到时的原话。改成摘要，听起来就不同了。'),
('CP-MIRA','Then keep the letter in your own cover. We do not need to make a public notice out of it.','那就留在你自己的封套里，不用变成公共公告。'),
('CP-LINA','I agreed to talk about keeping it. That is as far as I want this conversation to go.','我同意谈怎么保存它。这次我只想谈到这里。'),
('CP-MIRA','Understood. I will not copy a sentence or add the sender to a shared catalogue.','明白。我不会抄一句，也不会把寄信人记到共享书目里。'),
('CP-LINA','Could the outside just say that it belongs to me?','外面只写它是我的，可以吗？'),
('CP-MIRA','Yes. Your own label can help you find it without inviting everyone else to read it.','可以。你自己的标签能让你找到它，不必邀请所有人来看。'),
('CP-LINA','That feels better. I can put it away without pretending it is less important to me.','这样好些。我能把它收起来，也不用装作它对我没那么重要。')], 'private_reply_kept_by_lina_no_public_copy','lina_reply_retention')
add('BAN-LINA-TESS-1',[
('CP-LINA','At sea I can watch the surface as well as the sail. What do you watch in a narrow pass?','海上我能看水面，也能看帆。狭窄山口你看什么？'),
('CP-TESS','Dust, loose grass, and the places where the air changes after a bend. One patch can be quiet while the next is rough.','灰、松草，还有转弯后风变的地方。这一块安静，下一块可能很乱。'),
('CP-LINA','So I should not carry a reading from an open slope straight into the pass.','看来不能把开阔坡地的读数直接用到山口里。'),
('CP-TESS','Nor assume the calm spot will stay calm. I write where I stood before I write what I thought.','也别假定无风处一直无风。我先写站在哪里，再写我的判断。'),
('CP-LINA','We do the same on the deck, when the cabin interrupts the air.','甲板上船舱挡住气流时，我们也这样。'),
('CP-TESS','Then we have a useful habit in common. It still leaves us needing local measurements for a real journey.','那我们有个共同的好习惯。真正赶路时，还是需要当地测量。')], 'two_observation_contexts_compared_no_corridor_opened')
add('BAN-LINA-TESS-2',[
('CP-TESS','There is a sheltered bench along that ordinary footpath. People use it to sort their bags.','那条普通步道旁有张避风长凳，人们用来整理行李。'),
('CP-LINA','The map shows the path but no place to stop. Would the bench deserve a mark?','地图画了路，没画停歇处。长凳值得标吗？'),
('CP-TESS','A small one, with a date. It is useful to a tired walker, though it is not a refuge for every emergency.','标个小点，写日期。对疲惫行人有用，但并不是每种紧急情况的避难处。'),
('CP-LINA','I will leave room for someone to say the bench has moved.','我留地方，让人能补充长凳搬走了。'),
('CP-TESS','And say it is an ordinary stop. Nobody has checked it as a cargo holding point.','也写明是普通休息点。没人核查过它能不能停放货物。'),
('CP-LINA','A map can help someone rest without making another promise for the harbour office.','地图能帮人歇脚，不必顺手替港务办公室许诺。'),
('CP-TESS','That would be a pleasant sort of useful.','这种有用，倒让人舒服。')], 'ordinary_rest_note_added_not_safe_return_anchor')
add('BAN-OREN-MIRA-1',[
('CP-OREN','Would a cat in this empty margin interfere with your filing system?','在这块空白画只猫，会妨碍你归档吗？'),
('CP-MIRA','Is it a manufacturer mark, a warning, or just a cat?','它是制造商标志、警告，还是就是一只猫？'),
('CP-OREN','Just a cat. I was waiting for the glue to set.','就是猫。我在等胶干。'),
('CP-MIRA','Then leave the instructions visible and mark the drawing as yours. A later reader should not have to guess its authority.','那别遮说明，注明是你画的。后来读的人不该猜它有没有正式含义。'),
('CP-OREN','Personal drawing, no technical meaning. That is a rather official sentence for a cat.','个人绘画，无技术含义。对一只猫来说，这句话挺正式。'),
('CP-MIRA','You can write it smaller. We should still be able to tell the cat from the actual warning beside it.','可以写小一点。但还得能分清它和旁边真正的警告。'),
('CP-OREN','Agreed. I will give the warning the larger box.','好。警告的框画大一点。')], 'personal_cat_drawing_separate_from_instruction')
add('BAN-OREN-MIRA-2',[
('CP-OREN','I wrote the wrong size on this spare drawer. I could remove the whole label and start again.','我把备用抽屉的尺寸写错了，可以整张撕掉重写。'),
('CP-MIRA','Has someone already used it to sort parts?','已经有人照它分零件了吗？'),
('CP-OREN','Yes. Two pieces are in the wrong place now. I have moved them back.','有。两块放错了，我已放回去。'),
('CP-MIRA','Then keep a dated correction. It lets the next person understand why their earlier count disagrees.','那保留带日期的更正。下个人就能明白为什么先前数目对不上。'),
('CP-OREN','The old number is embarrassing, but crossing it out is quick enough.','旧数字有点丢人，不过划掉也不费事。'),
('CP-MIRA','And the correction says what you did. It does not have to make a claim about every other drawer.','更正也说明你做了什么。不必顺便对每个别的抽屉下结论。'),
('CP-OREN','I will check this drawer once more, then leave the two dates where people can read them.','我再查一次这个抽屉，然后把两个日期都留在看得见的地方。')], 'ordinary_drawer_error_corrected_original_label_retained')
add('BAN-OREN-TESS-1',[
('CP-OREN','If the tools face the same way, I can see which one is missing at a glance.','工具朝同一边放，我一眼就能看出缺哪件。'),
('CP-TESS','In the workshop, yes. On a narrow path I need the one I can reach without putting the bag down.','工坊里是。窄路上，我得拿到不用放下包就能取的那件。'),
('CP-OREN','Then the workshop order is not necessarily the carrying order.','那工坊里的顺序，未必适合携带。'),
('CP-TESS','Keep a list of what goes out. Let the travelling set have its own pockets. We can return it to your frame afterwards.','出门时列个表。随身工具用自己的袋格。回来再放你的框里。'),
('CP-OREN','I could label the pockets so they do not end up full of loose screws.','我可以给袋格贴标签，免得最后只剩一堆松螺钉。'),
('CP-TESS','Please leave one pocket empty. I always find something I need to carry back.','请留一个空格。我总会发现有东西得带回来。'),
('CP-OREN','An empty pocket on the list. I can live with that.','空格也列到表上。这我能接受。')], 'ordinary_travel_tool_pockets_agreed_not_inventory_grant')
add('BAN-OREN-TESS-2',[
('CP-TESS','This note says you will help whenever the hinge sticks. Does whenever include every night?','这便条说铰链一卡住你就来帮忙。“一”包括每个夜里吗？'),
('CP-OREN','I meant the next maintenance visit. I have written a larger promise than I can keep.','我指的是下一次维修来访。我写了个自己守不住的大承诺。'),
('CP-TESS','Then give the visit a window and leave an alternative contact for the hours outside it.','那写清来访窗口，再给窗口之外留另一种联系办法。'),
('CP-OREN','I should ask that contact first. I cannot volunteer someone by writing their name.','我得先问那个人。写上姓名，不能替人答应。'),
('CP-TESS','Exactly. Until they agree, say the alternative is unconfirmed. The note can tell the truth while we finish the arrangement.','是。在对方同意前，就写备选还未确认。安排没完成，便条也可以说真话。'),
('CP-OREN','Next visit only, other hours pending. It sounds smaller, but someone can actually use it.','仅下次来访，其余时间待定。听着小了，却真能用。')], 'ordinary_repair_note_bounded_no_new_workpost')
add('BAN-MIRA-TESS-1',[
('CP-MIRA','The old sign uses one name, and the people giving directions use another. Which should go on this card?','旧路牌一个名字，指路的人用另一个。卡上写哪个？'),
('CP-TESS','Both, with a short note about where each is used. Someone arriving with an older map still needs to find the road.','都写，简短说明各自用在哪里。拿旧地图来的人也得找到路。'),
('CP-MIRA','Should we call the old name incorrect?','要说旧名字错了吗？'),
('CP-TESS','Not from this conversation alone. We have heard the current everyday name; we have not changed the public register.','只凭这次聊天还不能。我们听到了现在日常的叫法，还没改公共登记。'),
('CP-MIRA','Then I will date the note and keep the two sources separate.','那我注明日期，把两个来源分开。'),
('CP-TESS','That gives people a way to ask a better question instead of losing their way while we settle the wording.','这样人们能问得更明白，不必等我们把措辞定完才能不迷路。')], 'two_names_on_ordinary_reference_card_no_register_change')
add('BAN-MIRA-TESS-2',[
('CP-TESS','I have read this storage rule twice. I still cannot tell where the wet boots go.','收纳规则读两遍了，我还是不知道湿靴子放哪里。'),
('CP-MIRA','The sentence says equipment requiring ventilation. That is rather far from a pair of boots.','句子写的是“需要通风的器材”。离一双靴子有点远。'),
('CP-TESS','Could we write wet boots on the rack label, and keep the full rule beside it?','架上直接写“湿靴子”，完整规则放旁边好吗？'),
('CP-MIRA','Yes, with a picture showing the gap below them. The shorter instruction should preserve the actual requirement.','好，再画出下面要留的空隙。短说明仍得保留真正的要求。'),
('CP-TESS','I will ask the next person to use it without hearing us explain it first.','我请下个人自己照它放，不先听我们解释。'),
('CP-MIRA','That will tell us something useful. If they choose the wrong shelf, we can improve the label rather than complain about the reader.','这样能知道些有用的事。要是放错架，我们可以改标签，不急着怪读的人。'),
('CP-TESS','Then this pair of boots can be our first trial. They have waited long enough.','那先拿这双试。它们等得够久了。')], 'ordinary_boot_rack_instruction_made_readable')
chair=(ROOT/'reference/part09/materials/NPC-MAT-06_Chair_Banter.md').read_text(encoding='utf-8')
chair_rows=re.findall(r'\*\*(Lina|Oren):\*\* (.+)',chair)
chair_zh=['我以为你昨天把椅子修好了。','修好了。今天我让它安静点。','没人坐的时候，它可安静了。','那不是我要测试的条件。','那就写个警告：适合永远不坐下的人安全使用。','或者你拿住这一块，我拧紧另一边。','好。但修完以后得让我们用。你不能把每件修好的东西都留成未完的实验。','同意。等我把最后一颗螺钉装回去，你再坐。']
add('BAN-LINA-OREN-1',[(('CP-LINA' if n=='Lina' else 'CP-OREN'),en,zh) for (n,en),zh in zip(chair_rows,chair_zh)],'ordinary_chair_available_after_last_screw')
assert len(scripts)==24 and {s['id'] for s in seeds}==set(scripts)
items=[]
for s in seeds:
    d=scripts[s['id']]; spot={'HUB-S01':'HUB-S01.harbor_view','HUB-S02':'HUB-S02.archive','HUB-S03':'HUB-S03.workbench'}[s['scene']]
    rows=[dict(id=f"{s['id']}-{i+1}",actor=a,en=en,zh=zh) for i,(a,en,zh) in enumerate(d['rows'])]
    assert len(rows)>=6 and all(r['actor'] in s['actors']+['HERO'] for r in rows)
    items.append(dict(id=s['id'],content_version='vessel-life-1',title_zh=s['premise'],scene=s['scene'],spot=spot,actors=s['actors'],scope=d['gate'],progress_gate=d['progress'] or '',public_fields=['ordinary_object_or_observation'],private_text_included=False,
      source_origin='existing_original' if s['id']=='BAN-LINA-OREN-1' else 'original_candidate',source_locator='reference/part09/materials/NPC-MAT-06_Chair_Banter.md' if s['id']=='BAN-LINA-OREN-1' else 'content/source/part09.json#/banter_seeds/'+str(seeds.index(s)),
      paragraphs=rows,consequence=d['consequence'],reward=0,review_status='original_unreviewed',human_review='human_pending',audio_asset=''))
package=dict(content_version='vessel-life-1',player_projection=True,scope='original_ship_life_candidate',review_status='original_unreviewed',human_review='human_pending',scripts=items)
(OUT/'learner_package.json').write_text(json.dumps(package,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
permissions=[]
for i in items:
    permissions.append(dict(id=i['id'],scene=i['scene'],spot=i['spot'],actors=i['actors'],current_actor_requirements=['actual_accept_invitation_receipt','Joined','ShipStandby','not_work_post','not_operator_duty','actual_safe_walk_to_original_scene','all_actor_and_foreground_leases'],source_scope=i['scope'],progress=i['progress_gate'],
      private_dependency='actual_collected_current_Lina_reply_and_current_per_recipient_disclosure_grant' if i['scope']!='public' else None,unknown_policy='deny_instead_of_infer',presentation='manual_next_line_only; pause checkpoint; complete once per version; one new scene per actual boarding return; replay readonly with current scope recheck',world_reward=0))
(OUT/'permissions.author_only.json').write_text(json.dumps(dict(content_version='vessel-life-1',source='content/source/part09.json#/banter_seeds',original_count=24,full_original_reused=1,original_candidates=23,permissions=permissions,chair_original_sha256=hashlib.sha256(chair.encode()).hexdigest(),remaining=['human language 6.5 review','human timing 30–90sec','actor voice and recording rights','native IME/accessibility human review']),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(OUT/'production_cards.author_only.md').write_text('''# 船上生活候选 v1\n\n24个原槽保持人物与场景。23个完整原创双语脚本；椅子段英文逐字保留NPC-MAT-06，中文另有辅助候选。没有取得来源就不显示正文。\n\n每条：实际同一微光号、当前招募来源、当前可用、无岗位/值守冲突、真实几何步行、全体人物与前景共同占用；玩家手动推进，暂停持久化到当前句，恢复重新核当前角色与范围。一次实际登船只展示一个新场景；完成后只读回看不再产生后果。故意暂缓、拒绝或资料尚未取得不会阻断主线或结局。\n\n公共话题邀请只授权此版本的日常物件/观察，不授权档案、家史或私信。BAN-LINA-2与BAN-LINA-MIRA-2须已实际取得本人回信及当下的有限接收方范围；前者只谈折纸，后者只谈保留原话，正文始终不复制信内文、住址或弟弟身份。撤回则下一句与回看均拒绝。\n\nBAN-OREN-1另须真实CQ-OREN02独立复核来源与已获旧工具框；尚未获框时不制造过去。其他片段只生产当前小事件：普通海图板、空废封套、无载备用绳、普通杯子、工具收纳。没有海路、保险避难点、塔站证据、六塔资源、隐秘真相或新奖励。\n\n运行接口及正常测试待统一串行验证。language6.5、真人30–90秒、配音/声音权利、物理输入法与可访问性均待审。书面文本不代表已经播放音轨。\n''',encoding='utf-8')
print(json.dumps({'scripts':len(items),'complete_candidate':23,'existing_original':1,'english_words':sum(len(r['en'].split()) for i in items for r in i['paragraphs']),'human_signoff':None}))
